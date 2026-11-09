using Microsoft.Extensions.Logging;
using OrcamentoPagamento.Domain.Commands;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Enums;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using OrcamentoPagamento.Domain.Interfaces.Repositories;
using OrcamentoPagamento.Domain.Interfaces.Services;
using OrcamentoPagamento.Domain.Services.Base;

namespace OrcamentoPagamento.Domain.Services
{
    public class OrcamentoService : BaseService, IOrcamentoService
    {
        /// <summary>Tipos de evento publicados por este serviço.</summary>
        public const string EventoOrcamentoCalculado = "orcamento.calculado";
        public const string EventoOrcamentoAjustado = "orcamento.ajustado";
        public const string EventoOrcamentoRemovido = "orcamento.removido";

        private readonly IOrcamentoRepository _orcamentoRepository;
        private readonly IPublicadorEventos _publicador;
        private readonly IPagamentoService _pagamentoService;
        private readonly ILogger<OrcamentoService> _logger;

        public OrcamentoService(
            ILogger<OrcamentoService> logger,
            INotificadorService notificadorService,
            IOrcamentoRepository orcamentoRepository,
            IPublicadorEventos publicador,
            IPagamentoService pagamentoService)
            : base(notificadorService)
        {
            _logger = logger;
            _orcamentoRepository = orcamentoRepository;
            _publicador = publicador;
            _pagamentoService = pagamentoService;
        }

        /// <summary>
        /// Cria ou atualiza o orçamento a partir dos itens recebidos no comando.
        /// Diferente do monólito, não há consulta à OS nem ao estoque: a categoria de
        /// cada item já chega resolvida, o que mantém o serviço autônomo.
        /// Retorna null quando o evento é descartado por idempotência.
        /// </summary>
        public async Task<Orcamento?> CalcularAsync(CalcularOrcamentoCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                if (command.OrdemServicoId <= 0)
                    throw new ArgumentException("OrdemServicoId é obrigatório para calcular o orçamento.");

                var orcamento = await _orcamentoRepository.ObterPorOrdemServicoIdAsync(command.OrdemServicoId);
                var novo = orcamento is null;

                if (!novo && orcamento!.DeveDescartarEvento(command.EventoId, command.OcorridoEm))
                {
                    _logger.LogInformation(
                        "Evento {EventoId} descartado para a OS {OrdemServicoId}: ja aplicado ou mais antigo que o ultimo processado ({UltimoEventoEm:o}).",
                        command.EventoId, command.OrdemServicoId, orcamento.UltimoEventoEm);
                    return null;
                }

                orcamento ??= new Orcamento { OrdemServicoId = command.OrdemServicoId };

                // Guardado antes do recálculo: é a comparação que decide se algum pagamento
                // pendente deixou de valer. Ver docs/pagamento.md, seção 3.
                var valorAnterior = orcamento.ValorTotalGeral;

                // Substituição completa dos itens: o evento carrega o estado atual da OS,
                // não um delta. Reconciliar item a item exigiria ordenação garantida.
                orcamento.Itens.Clear();

                foreach (var item in command.Itens)
                {
                    orcamento.Itens.Add(new ItemOrcamento
                    {
                        ItemOrigemId = item.ItemOrigemId,
                        Descricao = string.IsNullOrWhiteSpace(item.Descricao) ? "(sem descricao)" : item.Descricao,
                        Categoria = item.Categoria,
                        Quantidade = item.Quantidade,
                        ValorUnitario = item.ValorUnitario
                    });
                }

                orcamento.RecalcularTotais();

                orcamento.DataGeracao = DateTime.UtcNow;
                orcamento.Validade = DateTime.UtcNow.AddDays(command.DiasValidade);
                orcamento.UltimoEventoId = command.EventoId;
                orcamento.UltimoEventoEm = command.OcorridoEm;

                // Um recálculo refaz os valores: o ajuste manual anterior deixa de valer,
                // mesmo comportamento do monólito de hoje.
                orcamento.AjustadoManualmente = false;
                orcamento.AjustadoEm = null;

                AplicarSnapshot(orcamento, command.OrdemServico);

                var persistido = novo
                    ? await _orcamentoRepository.AdicionarAsync(orcamento)
                    : await _orcamentoRepository.AtualizarAsync(orcamento);

                await CancelarPagamentosSeValorMudouAsync(persistido, valorAnterior, novo, cancellationToken);

                await PublicarAsync(EventoOrcamentoCalculado, persistido, cancellationToken);

                return persistido;
            }
            catch (Exception ex)
            {
                Notificar(ex, "Ocorreu um erro no metodo OrcamentoService:CalcularAsync", _logger);
                throw;
            }
        }

        /// <summary>
        /// Ajuste manual dos valores. O total geral é sempre a soma das categorias, para
        /// não aceitar um orçamento internamente inconsistente.
        /// </summary>
        public async Task<Orcamento> AtualizarManualAsync(
            int id,
            decimal valorTotalPecas,
            decimal valorTotalInsumos,
            decimal valorTotalServicos,
            DateTime? validade,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var existente = await _orcamentoRepository.ObterPorIdAsync(id.ToString())
                    ?? throw new KeyNotFoundException($"Orcamento com Id {id} nao encontrado.");

                if (valorTotalPecas < 0 || valorTotalInsumos < 0 || valorTotalServicos < 0)
                    throw new ArgumentException("Os valores do orçamento não podem ser negativos.");

                var valorAnterior = existente.ValorTotalGeral;

                existente.ValorTotalPecas = valorTotalPecas;
                existente.ValorTotalInsumos = valorTotalInsumos;
                existente.ValorTotalServicos = valorTotalServicos;
                existente.ValorTotalGeral = valorTotalPecas + valorTotalInsumos + valorTotalServicos;
                existente.Validade = validade;
                existente.DataGeracao = DateTime.UtcNow;
                existente.AjustadoManualmente = true;
                existente.AjustadoEm = DateTime.UtcNow;

                var persistido = await _orcamentoRepository.AtualizarAsync(existente);

                // Ajuste manual muda o valor tanto quanto um recálculo — mesma invariante:
                // ninguém fica com pagamento pendente de um valor que não vale mais.
                await CancelarPagamentosSeValorMudouAsync(persistido, valorAnterior, orcamentoNovo: false, cancellationToken);

                await PublicarAsync(EventoOrcamentoAjustado, persistido, cancellationToken);

                return persistido;
            }
            catch (Exception ex)
            {
                Notificar(ex, "Ocorreu um erro no metodo OrcamentoService:AtualizarManualAsync", _logger);
                throw;
            }
        }

        public async Task<Orcamento?> ObterPorIdAsync(string id)
        {
            try
            {
                return await _orcamentoRepository.ObterPorIdAsync(id);
            }
            catch (Exception ex)
            {
                Notificar(ex, "Ocorreu um erro no metodo OrcamentoService:ObterPorIdAsync", _logger);
                throw;
            }
        }

        public async Task<Orcamento?> ObterPorOrdemServicoIdAsync(int ordemServicoId)
        {
            try
            {
                return await _orcamentoRepository.ObterPorOrdemServicoIdAsync(ordemServicoId);
            }
            catch (Exception ex)
            {
                Notificar(ex, "Ocorreu um erro no metodo OrcamentoService:ObterPorOrdemServicoIdAsync", _logger);
                throw;
            }
        }

        public async Task<Orcamento?> ObterComDetalhesAsync(int id)
        {
            try
            {
                return await _orcamentoRepository.ObterComDetalhesAsync(id);
            }
            catch (Exception ex)
            {
                Notificar(ex, "Ocorreu um erro no metodo OrcamentoService:ObterComDetalhesAsync", _logger);
                throw;
            }
        }

        public async Task RemoverAsync(string id, CancellationToken cancellationToken = default)
        {
            try
            {
                var existente = await _orcamentoRepository.ObterPorIdAsync(id)
                    ?? throw new KeyNotFoundException($"Orcamento com Id {id} nao encontrado.");

                await _orcamentoRepository.RemoverAsync(id);

                await PublicarAsync(EventoOrcamentoRemovido, existente, cancellationToken);
            }
            catch (Exception ex)
            {
                Notificar(ex, "Ocorreu um erro no metodo OrcamentoService:RemoverAsync", _logger);
                throw;
            }
        }

        /// <summary>
        /// Um recálculo que muda o total invalida pagamento ainda em aberto: o cliente
        /// estaria pagando um valor que não é mais o do orçamento. Pagamento já aprovado
        /// não é tocado — divergência ali é estorno ou cobrança complementar, com decisão
        /// humana. Ver docs/pagamento.md, seção 3.
        ///
        /// Isso roda no mesmo serviço, no mesmo banco e na mesma requisição do recálculo.
        /// Com pagamento em outro serviço, esta regra exigiria saga.
        /// </summary>
        private async Task CancelarPagamentosSeValorMudouAsync(
            Orcamento orcamento,
            decimal valorAnterior,
            bool orcamentoNovo,
            CancellationToken cancellationToken)
        {
            // Orçamento novo não tem pagamento, e valor igual não invalida nada.
            if (orcamentoNovo || valorAnterior == orcamento.ValorTotalGeral)
                return;

            var cancelados = await _pagamentoService.CancelarEmAbertoAsync(
                orcamento.Id,
                MotivoCancelamento.ValorDoOrcamentoAlterado,
                cancellationToken);

            if (cancelados > 0)
                _logger.LogInformation(
                    "Orcamento {OrcamentoId} mudou de {ValorAnterior} para {ValorNovo}: {Cancelados} pagamento(s) em aberto cancelado(s).",
                    orcamento.Id, valorAnterior, orcamento.ValorTotalGeral, cancelados);
        }

        private static void AplicarSnapshot(Orcamento orcamento, OrdemServicoDadosCommand? dados)
        {
            if (dados is null)
                return;

            orcamento.OrdemServicoSnapshot ??= new OrdemServicoSnapshot();

            var snapshot = orcamento.OrdemServicoSnapshot;
            snapshot.DescricaoProblema = dados.DescricaoProblema;
            snapshot.StatusOrdemServico = dados.Status;
            snapshot.ClienteNome = dados.ClienteNome;
            snapshot.ClienteEmail = dados.ClienteEmail;
            snapshot.ClienteTelefone = dados.ClienteTelefone;
            snapshot.VeiculoMarca = dados.VeiculoMarca;
            snapshot.VeiculoModelo = dados.VeiculoModelo;
            snapshot.VeiculoPlaca = dados.VeiculoPlaca;
            snapshot.VeiculoAno = dados.VeiculoAno;
            snapshot.AtualizadoEm = DateTime.UtcNow;
        }

        /// <summary>
        /// Publicação best-effort: o orçamento já está gravado, então uma falha no SNS não
        /// desfaz o cálculo. Ela é registrada e notificada, como o monólito faz com o SMTP.
        /// </summary>
        private async Task PublicarAsync(string tipoEvento, Orcamento orcamento, CancellationToken cancellationToken)
        {
            try
            {
                await _publicador.PublicarAsync(tipoEvento, new
                {
                    orcamentoId = orcamento.Id,
                    ordemServicoId = orcamento.OrdemServicoId,
                    valorTotalPecas = orcamento.ValorTotalPecas,
                    valorTotalInsumos = orcamento.ValorTotalInsumos,
                    valorTotalServicos = orcamento.ValorTotalServicos,
                    valorTotalGeral = orcamento.ValorTotalGeral,
                    dataGeracao = orcamento.DataGeracao,
                    validade = orcamento.Validade,
                    ajustadoManualmente = orcamento.AjustadoManualmente
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                Notificar(ex, $"Falha ao publicar o evento {tipoEvento} do orcamento {orcamento.Id}", _logger);
            }
        }
    }
}
