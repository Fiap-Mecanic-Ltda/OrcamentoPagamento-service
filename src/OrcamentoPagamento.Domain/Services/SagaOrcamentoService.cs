using MechanicLtda.Contratos;
using Microsoft.Extensions.Logging;
using OrcamentoPagamento.Domain.Commands;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Enums;
using OrcamentoPagamento.Domain.Exceptions;
using OrcamentoPagamento.Domain.Interfaces.Gateways;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using OrcamentoPagamento.Domain.Interfaces.Repositories;
using OrcamentoPagamento.Domain.Interfaces.Services;
using OrcamentoPagamento.Domain.Messaging;
using OrcamentoPagamento.Domain.Services.Base;
using System.Security.Cryptography;
using System.Text;

namespace OrcamentoPagamento.Domain.Services
{
    /// <summary>
    /// Billing na Saga da OS. Ver docs/contratos-eventos.md.
    ///
    /// Idempotência (o RabbitMQ entrega ao menos uma vez): a chave é o correlationId da Saga
    /// mais o nome da mensagem, guardada no próprio estado. O orçamento grava o correlationId
    /// que o gerou; cancelamento e estorno gravam o status final. Uma reentrega encontra esse
    /// estado e só republica a mesma resposta, que o OS Service ignora se já tiver recebido.
    ///
    /// Erro de negócio vira mensagem de falha (OrcamentoFalhou). Erro técnico (banco, provedor
    /// de pagamento) sobe como exceção: o consumidor tenta de novo e, esgotado, a mensagem vai
    /// para a fila _error.
    /// </summary>
    public class SagaOrcamentoService : BaseService, ISagaOrcamentoService
    {
        private readonly IOrcamentoService _orcamentoService;
        private readonly IOrcamentoRepository _orcamentoRepository;
        private readonly IPagamentoRepository _pagamentoRepository;
        private readonly IPagamentoService _pagamentoService;
        private readonly IGatewayPagamento _gateway;
        private readonly IPublicadorSaga _publicador;
        private readonly ILogger<SagaOrcamentoService> _logger;

        public SagaOrcamentoService(
            ILogger<SagaOrcamentoService> logger,
            INotificadorService notificadorService,
            IOrcamentoService orcamentoService,
            IOrcamentoRepository orcamentoRepository,
            IPagamentoRepository pagamentoRepository,
            IPagamentoService pagamentoService,
            IGatewayPagamento gateway,
            IPublicadorSaga publicador)
            : base(notificadorService)
        {
            _logger = logger;
            _orcamentoService = orcamentoService;
            _orcamentoRepository = orcamentoRepository;
            _pagamentoRepository = pagamentoRepository;
            _pagamentoService = pagamentoService;
            _gateway = gateway;
            _publicador = publicador;
        }

        // ─── GerarOrcamento ────────────────────────────────────────────────────

        public async Task TratarAsync(GerarOrcamento comando, CancellationToken cancellationToken = default)
        {
            var existente = await _orcamentoRepository.ObterPorOrdemServicoIdAsync(comando.OrdemServicoId);

            if (existente is not null && existente.CorrelationId == comando.CorrelationId)
            {
                // Reentrega do mesmo comando da mesma Saga: responde de novo, sem recalcular.
                if (!existente.EstaEmAberto() && existente.Status != StatusOrcamento.Pago)
                {
                    _logger.LogInformation(
                        "GerarOrcamento repetido para a OS {OrdemServicoId}, cujo orcamento {OrcamentoId} ja esta {Status}: ignorado.",
                        comando.OrdemServicoId, existente.Id, existente.Status);
                    return;
                }

                _logger.LogInformation(
                    "GerarOrcamento repetido para a OS {OrdemServicoId}: republicando OrcamentoGerado do orcamento {OrcamentoId}.",
                    comando.OrdemServicoId, existente.Id);
                await PublicarGeradoAsync(comando, existente, cancellationToken);
                return;
            }

            var (itens, erro) = ConverterItens(comando.Itens);
            if (erro is not null)
            {
                _logger.LogWarning(
                    "Orcamento da OS {OrdemServicoId} nao gerado: {Motivo}", comando.OrdemServicoId, erro);
                await _publicador.PublicarAsync(MensagensSaga.Responder(comando, new OrcamentoFalhou { Motivo = erro }), cancellationToken);
                return;
            }

            var orcamento = await _orcamentoService.CalcularAsync(new CalcularOrcamentoCommand
            {
                // A idempotência da Saga é o correlationId (acima); EventoId e OcorridoEm só
                // valem para a rota manual de recálculo, então nunca descartam este cálculo.
                EventoId = Guid.NewGuid(),
                OcorridoEm = DateTime.UtcNow,
                OrdemServicoId = comando.OrdemServicoId,
                CorrelationId = comando.CorrelationId,
                Itens = itens,
                // Telefone, problema e veículo são opcionais no contrato: o que vier nulo fica
                // como "não informado" no PDF.
                OrdemServico = new OrdemServicoDadosCommand
                {
                    Status = "AguardandoOrcamento",
                    DescricaoProblema = comando.DescricaoProblema,
                    ClienteNome = comando.NomeCliente,
                    ClienteEmail = comando.EmailCliente,
                    ClienteTelefone = comando.TelefoneCliente,
                    VeiculoPlaca = comando.Veiculo?.Placa,
                    VeiculoMarca = comando.Veiculo?.Marca,
                    VeiculoModelo = comando.Veiculo?.Modelo,
                    VeiculoAno = comando.Veiculo?.Ano
                }
            }, cancellationToken)
                ?? throw new InvalidOperationException($"O calculo do orcamento da OS {comando.OrdemServicoId} foi descartado.");

            // O envio do e-mail de aprovação ao cliente ainda não está implementado neste
            // serviço; o cliente vê o orçamento e paga pelo Web.
            _logger.LogInformation(
                "Orcamento {OrcamentoId} da OS {OrdemServicoId} gerado: total {Total}, validade {Validade:o}, cliente {Email}.",
                orcamento.Id, orcamento.OrdemServicoId, orcamento.ValorTotalGeral, orcamento.Validade, comando.EmailCliente);

            await PublicarGeradoAsync(comando, orcamento, cancellationToken);
        }

        private Task PublicarGeradoAsync(GerarOrcamento comando, Orcamento orcamento, CancellationToken cancellationToken) =>
            _publicador.PublicarAsync(MensagensSaga.Responder(comando, new OrcamentoGerado
            {
                OrcamentoId = orcamento.Id,
                ValorTotal = orcamento.ValorTotalGeral,
                Validade = orcamento.Validade
            }), cancellationToken);

        /// <summary>
        /// Itens do contrato (tipo como texto: Peca, Insumo, Servico) para o cálculo. Item
        /// inválido ou orçamento sem valor é erro de negócio: vira OrcamentoFalhou e o OS
        /// Service pede um novo diagnóstico.
        /// </summary>
        internal static (IReadOnlyCollection<ItemCalculoCommand> Itens, string? Erro) ConverterItens(IReadOnlyList<ItemOrdemServico>? itens)
        {
            if (itens is null || itens.Count == 0)
                return ([], "O diagnostico nao trouxe itens para orcar.");

            var convertidos = new List<ItemCalculoCommand>(itens.Count);

            foreach (var item in itens)
            {
                if (!Enum.TryParse<CategoriaItem>(item.Tipo?.Trim(), ignoreCase: true, out var categoria)
                    || !Enum.IsDefined(categoria)
                    || int.TryParse(item.Tipo, out _))
                    return ([], $"Tipo de item invalido: '{item.Tipo}'. Esperado Peca, Insumo ou Servico.");

                if (item.Quantidade <= 0)
                    return ([], $"Quantidade invalida ({item.Quantidade}) no item '{item.Descricao}'.");

                if (item.ValorUnitario < 0)
                    return ([], $"Valor unitario negativo no item '{item.Descricao}'.");

                convertidos.Add(new ItemCalculoCommand
                {
                    Descricao = item.Descricao,
                    Categoria = categoria,
                    Quantidade = item.Quantidade,
                    ValorUnitario = item.ValorUnitario
                });
            }

            if (convertidos.Sum(i => i.Quantidade * i.ValorUnitario) <= 0)
                return ([], "O orcamento ficou com valor zerado e nao pode ser cobrado.");

            return (convertidos, null);
        }

        // ─── CancelarOrcamento ─────────────────────────────────────────────────

        public async Task TratarAsync(CancelarOrcamento comando, CancellationToken cancellationToken = default)
        {
            var orcamento = await _orcamentoRepository.ObterPorOrdemServicoIdAsync(comando.OrdemServicoId);
            var orcamentoId = orcamento?.Id ?? comando.OrcamentoId;

            if (orcamento is null)
            {
                // Nada a cancelar, mas a compensação precisa da confirmação para terminar.
                _logger.LogWarning(
                    "CancelarOrcamento da OS {OrdemServicoId}: nenhum orcamento encontrado. Confirmando mesmo assim.",
                    comando.OrdemServicoId);
            }
            else if (orcamento.ParticipaDaSaga() && orcamento.CorrelationId != comando.CorrelationId)
            {
                // Comando de uma Saga antiga, depois que uma nova reabriu o orçamento.
                _logger.LogWarning(
                    "CancelarOrcamento da Saga {Saga} ignorado: o orcamento {OrcamentoId} pertence a Saga {SagaAtual}.",
                    comando.CorrelationId, orcamento.Id, orcamento.CorrelationId);
            }
            else if (orcamento.Status != StatusOrcamento.Cancelado)
            {
                orcamento.Encerrar(StatusOrcamento.Cancelado, comando.Motivo);
                await _orcamentoRepository.AtualizarAsync(orcamento);
                await _pagamentoService.CancelarEmAbertoAsync(orcamento.Id, MotivoCancelamento.CompensacaoDaSaga, cancellationToken);

                _logger.LogInformation(
                    "Orcamento {OrcamentoId} da OS {OrdemServicoId} cancelado pela Saga: {Motivo}.",
                    orcamento.Id, orcamento.OrdemServicoId, comando.Motivo);
            }

            await _publicador.PublicarAsync(
                MensagensSaga.Responder(comando, new OrcamentoCancelado { OrcamentoId = orcamentoId }), cancellationToken);
        }

        // ─── EstornarPagamento ─────────────────────────────────────────────────

        public async Task TratarAsync(EstornarPagamento comando, CancellationToken cancellationToken = default)
        {
            var pagamento = await _pagamentoRepository.ObterPorIdAsync(comando.PagamentoId.ToString());

            if (pagamento is null || pagamento.OrdemServicoId != comando.OrdemServicoId)
            {
                _logger.LogWarning(
                    "EstornarPagamento: pagamento {PagamentoId} nao encontrado para a OS {OrdemServicoId}. Confirmando mesmo assim.",
                    comando.PagamentoId, comando.OrdemServicoId);
            }
            else if (pagamento.EstaEmAberto())
            {
                // Ainda não foi cobrado: cancela em vez de devolver.
                pagamento.Cancelar(MotivoCancelamento.CompensacaoDaSaga);
                await _pagamentoRepository.AtualizarAsync(pagamento);
            }
            else if (!pagamento.SemValorADevolver())
            {
                await DevolverNoProvedorAsync(pagamento, comando, cancellationToken);
            }

            await _publicador.PublicarAsync(
                MensagensSaga.Responder(comando, new PagamentoEstornado { PagamentoId = comando.PagamentoId }), cancellationToken);
        }

        private async Task DevolverNoProvedorAsync(Pagamento pagamento, EstornarPagamento comando, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pagamento.IdExterno))
                throw new InvalidOperationException($"O pagamento {pagamento.Id} esta {pagamento.Status} sem IdExterno: estorno manual.");

            // Mesma chave em toda reentrega do comando: o provedor não devolve duas vezes.
            var chave = ChaveDeterministica($"EstornarPagamento#{comando.CorrelationId}#{pagamento.Id}");
            var resposta = await _gateway.EstornarAsync(pagamento.IdExterno, chave, cancellationToken);

            var aplicou = pagamento.AplicarStatusDoProvedor(
                resposta.Status, resposta.StatusDetalhe, resposta.IdExterno, resposta.MetodoPagamento,
                resposta.Parcelas, !resposta.ModoProducao, resposta.UltimaAtualizacao);

            // O provedor aceitou a devolução mas a consulta ainda mostra o status anterior.
            if (!pagamento.SemValorADevolver())
                aplicou = pagamento.RegistrarEstorno(resposta.StatusDetalhe ?? "refund_requested") || aplicou;

            if (aplicou)
                await _pagamentoRepository.AtualizarAsync(pagamento);

            _logger.LogInformation(
                "Pagamento {PagamentoId} da OS {OrdemServicoId} devolvido pela Saga: {Status} ({Motivo}).",
                pagamento.Id, pagamento.OrdemServicoId, pagamento.Status, comando.Motivo);
        }

        // ─── Aprovação e recusa pelo cliente (API) ─────────────────────────────

        public async Task<Orcamento> AprovarAsync(int orcamentoId, CancellationToken cancellationToken = default)
        {
            var orcamento = await ObterOrcamentoAsync(orcamentoId);

            if (!orcamento.EstaEmAberto())
                throw new OrcamentoIndisponivelException(orcamento.Id, orcamento.Status, "aprovacao");

            if (orcamento.Status == StatusOrcamento.Aberto)
            {
                orcamento.Status = StatusOrcamento.Aprovado;
                await _orcamentoRepository.AtualizarAsync(orcamento);
            }

            // Só informativo para a OS: a aprovação que conta é o pagamento confirmado.
            if (orcamento.ParticipaDaSaga())
                await _publicador.PublicarAsync(
                    MensagensSaga.DoOrcamento(orcamento, new OrcamentoAprovado { OrcamentoId = orcamento.Id }), cancellationToken);

            return orcamento;
        }

        public async Task<Orcamento> RecusarAsync(int orcamentoId, string? motivo, CancellationToken cancellationToken = default)
        {
            var orcamento = await ObterOrcamentoAsync(orcamentoId);

            // Repetir a recusa só republica (ex.: a primeira publicação falhou).
            if (orcamento.Status != StatusOrcamento.Recusado)
            {
                if (!orcamento.EstaEmAberto())
                    throw new OrcamentoIndisponivelException(orcamento.Id, orcamento.Status, "recusa");

                orcamento.Encerrar(StatusOrcamento.Recusado, motivo);
                await _orcamentoRepository.AtualizarAsync(orcamento);
                await _pagamentoService.CancelarEmAbertoAsync(orcamento.Id, MotivoCancelamento.OrcamentoRecusado, cancellationToken);
            }

            if (orcamento.ParticipaDaSaga())
                await _publicador.PublicarAsync(
                    MensagensSaga.DoOrcamento(orcamento, new OrcamentoRecusado { OrcamentoId = orcamento.Id, Motivo = orcamento.MotivoStatus }),
                    cancellationToken);

            return orcamento;
        }

        private async Task<Orcamento> ObterOrcamentoAsync(int orcamentoId) =>
            await _orcamentoRepository.ObterPorIdAsync(orcamentoId.ToString())
            ?? throw new KeyNotFoundException($"Orcamento com Id {orcamentoId} nao encontrado.");

        /// <summary>GUID estável a partir de um texto (chave de idempotência no provedor).</summary>
        internal static Guid ChaveDeterministica(string texto) =>
            new(SHA256.HashData(Encoding.UTF8.GetBytes(texto)).AsSpan(0, 16));
    }
}
