using Microsoft.Extensions.Logging;
using OrcamentoPagamento.Domain.Commands;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Enums;
using OrcamentoPagamento.Domain.Exceptions;
using OrcamentoPagamento.Domain.Interfaces.Gateways;
using OrcamentoPagamento.Domain.Interfaces.Messaging;
using OrcamentoPagamento.Domain.Interfaces.Repositories;
using OrcamentoPagamento.Domain.Interfaces.Services;
using OrcamentoPagamento.Domain.Services.Base;

namespace OrcamentoPagamento.Domain.Services
{
    public class PagamentoService : BaseService, IPagamentoService
    {
        public const string EventoPagamentoCriado = "pagamento.criado";
        public const string EventoPagamentoAprovado = "pagamento.aprovado";
        public const string EventoPagamentoRecusado = "pagamento.recusado";
        public const string EventoPagamentoCancelado = "pagamento.cancelado";
        public const string EventoPagamentoEstornado = "pagamento.estornado";

        private readonly IPagamentoRepository _pagamentoRepository;
        private readonly IOrcamentoRepository _orcamentoRepository;
        private readonly IGatewayPagamento _gateway;
        private readonly IPublicadorEventos _publicador;
        private readonly ILogger<PagamentoService> _logger;

        public PagamentoService(
            ILogger<PagamentoService> logger,
            INotificadorService notificadorService,
            IPagamentoRepository pagamentoRepository,
            IOrcamentoRepository orcamentoRepository,
            IGatewayPagamento gateway,
            IPublicadorEventos publicador)
            : base(notificadorService)
        {
            _logger = logger;
            _pagamentoRepository = pagamentoRepository;
            _orcamentoRepository = orcamentoRepository;
            _gateway = gateway;
            _publicador = publicador;
        }

        public async Task<ResultadoCriacaoPagamento> CriarAsync(CriarPagamentoCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                ValidarComando(command);

                // 1. Idempotência de entrada, antes de qualquer coisa: se o Web reenviou a
                //    mesma requisição, devolvemos o que já existe e não cobramos de novo.
                var existente = await _pagamentoRepository.ObterPorRequisicaoIdAsync(command.RequisicaoId);
                if (existente is not null)
                {
                    _logger.LogInformation(
                        "Requisicao {RequisicaoId} ja processada: devolvendo o pagamento {PagamentoId} sem nova cobranca.",
                        command.RequisicaoId, existente.Id);
                    return new ResultadoCriacaoPagamento(existente, jaExistia: true);
                }

                var orcamento = await _orcamentoRepository.ObterPorIdAsync(command.OrcamentoId.ToString())
                    ?? throw new KeyNotFoundException($"Orcamento com Id {command.OrcamentoId} nao encontrado.");

                // 2. Um orçamento não pode ser pago duas vezes.
                var aprovado = await _pagamentoRepository.ObterAprovadoPorOrcamentoIdAsync(orcamento.Id);
                if (aprovado is not null)
                    throw new PagamentoJaAprovadoException(orcamento.Id, aprovado.Id);

                if (orcamento.ValorTotalGeral <= 0)
                    throw new ArgumentException($"O orcamento {orcamento.Id} tem valor zerado e nao pode ser cobrado.");

                // 3. O valor é o do orçamento, congelado aqui. Nunca o que o cliente mandou.
                var pagamento = new Pagamento
                {
                    RequisicaoId = command.RequisicaoId,
                    OrcamentoId = orcamento.Id,
                    OrdemServicoId = orcamento.OrdemServicoId,
                    Valor = orcamento.ValorTotalGeral,
                    Status = StatusPagamento.Pendente,
                    Provedor = _gateway.Nome,
                    Parcelas = command.Parcelas,
                    MetodoPagamento = command.MetodoPagamento,
                    CriadoEm = DateTime.UtcNow,
                    AtualizadoEm = DateTime.UtcNow
                };

                var resposta = await _gateway.CriarAsync(new RequisicaoGatewayPagamento
                {
                    Valor = pagamento.Valor,
                    Token = command.Token,
                    Descricao = command.Descricao ?? $"Orcamento {orcamento.Id:D6} - OS {orcamento.OrdemServicoId}",
                    Parcelas = command.Parcelas,
                    MetodoPagamento = command.MetodoPagamento,
                    EmissorId = command.EmissorId,
                    PagadorEmail = command.PagadorEmail,
                    PagadorTipoDocumento = command.PagadorTipoDocumento,
                    PagadorDocumento = command.PagadorDocumento,
                    ReferenciaExterna = orcamento.Id.ToString(),
                    ChaveIdempotencia = command.RequisicaoId
                }, cancellationToken);

                // O pagamento nasce Pendente e recebe o estado do provedor pela mesma
                // máquina de estados do webhook — um caminho só para aplicar status.
                pagamento.Status = resposta.Status;
                pagamento.StatusDetalhe = resposta.StatusDetalhe;
                pagamento.IdExterno = resposta.IdExterno;
                pagamento.ModoTeste = !resposta.ModoProducao;
                pagamento.UltimaAtualizacaoProvedor = resposta.UltimaAtualizacao;

                if (!string.IsNullOrWhiteSpace(resposta.MetodoPagamento))
                    pagamento.MetodoPagamento = resposta.MetodoPagamento;

                if (resposta.Parcelas is > 0)
                    pagamento.Parcelas = resposta.Parcelas.Value;

                var persistido = await _pagamentoRepository.AdicionarAsync(pagamento);

                await PublicarAsync(EventoPagamentoCriado, persistido, cancellationToken);
                await PublicarEventoDeStatusAsync(persistido, cancellationToken);

                _logger.LogInformation(
                    "Pagamento {PagamentoId} criado para o orcamento {OrcamentoId}: {Status} ({StatusDetalhe}), valor {Valor}.",
                    persistido.Id, persistido.OrcamentoId, persistido.Status, persistido.StatusDetalhe, persistido.Valor);

                return new ResultadoCriacaoPagamento(persistido, jaExistia: false);
            }
            catch (Exception ex) when (ex is not KeyNotFoundException
                                       and not ArgumentException
                                       and not PagamentoJaAprovadoException
                                       and not GatewayPagamentoException)
            {
                Notificar(ex, "Ocorreu um erro no metodo PagamentoService:CriarAsync", _logger);
                throw;
            }
        }

        public async Task<Pagamento?> ObterPorIdAsync(int id)
        {
            try
            {
                return await _pagamentoRepository.ObterPorIdAsync(id.ToString());
            }
            catch (Exception ex)
            {
                Notificar(ex, "Ocorreu um erro no metodo PagamentoService:ObterPorIdAsync", _logger);
                throw;
            }
        }

        public async Task<IEnumerable<Pagamento>> ObterPorOrcamentoIdAsync(int orcamentoId)
        {
            try
            {
                return await _pagamentoRepository.ObterPorOrcamentoIdAsync(orcamentoId);
            }
            catch (Exception ex)
            {
                Notificar(ex, "Ocorreu um erro no metodo PagamentoService:ObterPorOrcamentoIdAsync", _logger);
                throw;
            }
        }

        public async Task<IEnumerable<Pagamento>> ObterPorOrdemServicoIdAsync(int ordemServicoId)
        {
            try
            {
                return await _pagamentoRepository.ObterPorOrdemServicoIdAsync(ordemServicoId);
            }
            catch (Exception ex)
            {
                Notificar(ex, "Ocorreu um erro no metodo PagamentoService:ObterPorOrdemServicoIdAsync", _logger);
                throw;
            }
        }

        public async Task<Pagamento?> SincronizarAsync(int id, CancellationToken cancellationToken = default)
        {
            var pagamento = await _pagamentoRepository.ObterPorIdAsync(id.ToString())
                ?? throw new KeyNotFoundException($"Pagamento com Id {id} nao encontrado.");

            return await AplicarEstadoDoProvedorAsync(pagamento, cancellationToken);
        }

        public async Task<Pagamento?> SincronizarPorIdExternoAsync(string idExterno, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(idExterno))
                return null;

            var pagamento = await _pagamentoRepository.ObterPorIdExternoAsync(idExterno);

            if (pagamento is null)
            {
                // Pode ser notificação de um pagamento que não é nosso, ou de um que a
                // criação ainda não gravou. O chamador responde 200 de qualquer forma:
                // devolver erro só faria o provedor reenviar para sempre.
                _logger.LogWarning(
                    "Notificacao recebida para o pagamento externo {IdExterno}, que nao existe neste servico.",
                    idExterno);
                return null;
            }

            return await AplicarEstadoDoProvedorAsync(pagamento, cancellationToken);
        }

        public async Task<int> CancelarEmAbertoAsync(int orcamentoId, string motivo, CancellationToken cancellationToken = default)
        {
            try
            {
                var emAberto = (await _pagamentoRepository.ObterEmAbertoPorOrcamentoIdAsync(orcamentoId)).ToList();

                if (emAberto.Count == 0)
                    return 0;

                var cancelados = 0;

                foreach (var pagamento in emAberto)
                {
                    if (!pagamento.Cancelar(motivo))
                        continue;

                    await _pagamentoRepository.AtualizarAsync(pagamento);
                    await PublicarAsync(EventoPagamentoCancelado, pagamento, cancellationToken);
                    cancelados++;

                    _logger.LogInformation(
                        "Pagamento {PagamentoId} do orcamento {OrcamentoId} cancelado: {Motivo}.",
                        pagamento.Id, orcamentoId, motivo);
                }

                return cancelados;
            }
            catch (Exception ex)
            {
                Notificar(ex, "Ocorreu um erro no metodo PagamentoService:CancelarEmAbertoAsync", _logger);
                throw;
            }
        }

        /// <summary>
        /// Consulta o provedor e aplica o resultado. O corpo da notificação nunca é fonte
        /// de verdade — é só o aviso de que algo mudou.
        /// </summary>
        private async Task<Pagamento?> AplicarEstadoDoProvedorAsync(Pagamento pagamento, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pagamento.IdExterno))
            {
                _logger.LogWarning(
                    "Pagamento {PagamentoId} nao tem IdExterno e nao pode ser sincronizado.", pagamento.Id);
                return null;
            }

            var resposta = await _gateway.ObterAsync(pagamento.IdExterno, cancellationToken);

            var statusAnterior = pagamento.Status;

            var mudou = pagamento.AplicarStatusDoProvedor(
                resposta.Status,
                resposta.StatusDetalhe,
                resposta.IdExterno,
                resposta.MetodoPagamento,
                resposta.Parcelas,
                !resposta.ModoProducao,
                resposta.UltimaAtualizacao);

            if (!mudou)
            {
                _logger.LogInformation(
                    "Pagamento {PagamentoId} sem mudanca: {StatusAtual} permanece (provedor informou {StatusProvedor}).",
                    pagamento.Id, statusAnterior, resposta.Status);
                return null;
            }

            await _pagamentoRepository.AtualizarAsync(pagamento);

            _logger.LogInformation(
                "Pagamento {PagamentoId}: {StatusAnterior} -> {StatusNovo} ({StatusDetalhe}).",
                pagamento.Id, statusAnterior, pagamento.Status, pagamento.StatusDetalhe);

            await PublicarEventoDeStatusAsync(pagamento, cancellationToken);

            return pagamento;
        }

        private static void ValidarComando(CriarPagamentoCommand command)
        {
            if (command.OrcamentoId <= 0)
                throw new ArgumentException("OrcamentoId e obrigatorio.");

            if (string.IsNullOrWhiteSpace(command.Token))
                throw new ArgumentException("O token do cartao e obrigatorio.");

            if (string.IsNullOrWhiteSpace(command.MetodoPagamento))
                throw new ArgumentException("O metodo de pagamento e obrigatorio.");

            if (string.IsNullOrWhiteSpace(command.PagadorEmail))
                throw new ArgumentException("O e-mail do pagador e obrigatorio.");

            if (command.Parcelas < 1)
                throw new ArgumentException("O numero de parcelas deve ser no minimo 1.");
        }

        private async Task PublicarEventoDeStatusAsync(Pagamento pagamento, CancellationToken cancellationToken)
        {
            var tipoEvento = pagamento.Status switch
            {
                StatusPagamento.Aprovado => EventoPagamentoAprovado,
                StatusPagamento.Recusado => EventoPagamentoRecusado,
                StatusPagamento.Cancelado => EventoPagamentoCancelado,
                StatusPagamento.Devolvido or StatusPagamento.EmDisputa or StatusPagamento.Estornado => EventoPagamentoEstornado,
                _ => null
            };

            if (tipoEvento is null)
                return;

            await PublicarAsync(tipoEvento, pagamento, cancellationToken);
        }

        /// <summary>
        /// Best-effort, igual ao orçamento: o pagamento já está gravado, então falha na
        /// publicação é registrada e notificada, não desfaz nada.
        /// </summary>
        private async Task PublicarAsync(string tipoEvento, Pagamento pagamento, CancellationToken cancellationToken)
        {
            try
            {
                await _publicador.PublicarAsync(tipoEvento, new
                {
                    pagamentoId = pagamento.Id,
                    orcamentoId = pagamento.OrcamentoId,
                    ordemServicoId = pagamento.OrdemServicoId,
                    valor = pagamento.Valor,
                    status = pagamento.Status.ToString(),
                    statusDetalhe = pagamento.StatusDetalhe,
                    provedor = pagamento.Provedor,
                    idExterno = pagamento.IdExterno,
                    motivoCancelamento = pagamento.MotivoCancelamento,
                    modoTeste = pagamento.ModoTeste,
                    atualizadoEm = pagamento.AtualizadoEm
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                Notificar(ex, $"Falha ao publicar o evento {tipoEvento} do pagamento {pagamento.Id}", _logger);
            }
        }
    }
}
