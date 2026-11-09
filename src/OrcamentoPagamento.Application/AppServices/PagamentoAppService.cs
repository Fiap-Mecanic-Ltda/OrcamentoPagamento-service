using AutoMapper;
using OrcamentoPagamento.Application.AppServices.Interfaces;
using OrcamentoPagamento.Application.DTOs;
using OrcamentoPagamento.Domain.Commands;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Exceptions;
using OrcamentoPagamento.Domain.Interfaces.Services;

namespace OrcamentoPagamento.Application.AppServices
{
    public class PagamentoAppService : IPagamentoAppService
    {
        private readonly IPagamentoService _pagamentoService;
        private readonly IMapper _mapper;

        public PagamentoAppService(IPagamentoService pagamentoService, IMapper mapper)
        {
            _pagamentoService = pagamentoService;
            _mapper = mapper;
        }

        public async Task<RespostaPagamento> CriarAsync(PagamentoCreateDto dto, CancellationToken cancellationToken = default)
        {
            try
            {
                var command = new CriarPagamentoCommand
                {
                    // Sem RequisicaoId do Web, geramos um — mas aí o retry do cliente não
                    // tem proteção contra cobrança dupla. O contrato pede que o Web mande.
                    RequisicaoId = dto.RequisicaoId is null || dto.RequisicaoId == Guid.Empty
                        ? Guid.NewGuid()
                        : dto.RequisicaoId.Value,
                    OrcamentoId = dto.OrcamentoId,
                    Token = dto.Token,
                    MetodoPagamento = dto.PaymentMethodId,
                    EmissorId = dto.IssuerId,
                    Parcelas = dto.Installments,
                    Descricao = dto.Descricao,
                    PagadorEmail = dto.Payer?.Email ?? string.Empty,
                    PagadorTipoDocumento = dto.Payer?.Identification?.Type,
                    PagadorDocumento = dto.Payer?.Identification?.Number
                };

                var resultado = await _pagamentoService.CriarAsync(command, cancellationToken);

                return RespostaPagamento.De(
                    resultado.JaExistia ? ResultadoPagamento.JaProcessado : ResultadoPagamento.Criado,
                    Mapear(resultado.Pagamento));
            }
            catch (KeyNotFoundException ex)
            {
                return RespostaPagamento.Erro(ResultadoPagamento.NaoEncontrado, ex.Message);
            }
            catch (PagamentoJaAprovadoException ex)
            {
                return RespostaPagamento.Erro(ResultadoPagamento.JaAprovado, ex.Message);
            }
            catch (ArgumentException ex)
            {
                return RespostaPagamento.Erro(ResultadoPagamento.DadosInvalidos, ex.Message);
            }
            catch (GatewayPagamentoException ex)
            {
                return RespostaPagamento.Erro(ResultadoPagamento.FalhaProvedor, ex.Message);
            }
            catch (Exception ex)
            {
                return RespostaPagamento.Erro(ResultadoPagamento.FalhaProvedor, ex.Message);
            }
        }

        public async Task<ResponseDto<PagamentoDto>> ObterPorIdAsync(int id)
        {
            var response = new ResponseDto<PagamentoDto>();
            try
            {
                var pagamento = await _pagamentoService.ObterPorIdAsync(id)
                    ?? throw new KeyNotFoundException($"Pagamento com Id {id} nao encontrado.");

                return response.setResponse(Mapear(pagamento));
            }
            catch (KeyNotFoundException ex) { return response.addError(ex.Message); }
            catch (Exception ex) { return response.addError(ex); }
        }

        public async Task<ResponseDto<IEnumerable<PagamentoDto>>> ObterPorOrcamentoIdAsync(int orcamentoId)
        {
            var response = new ResponseDto<IEnumerable<PagamentoDto>>();
            try
            {
                var pagamentos = await _pagamentoService.ObterPorOrcamentoIdAsync(orcamentoId);
                return response.setResponse(pagamentos.Select(Mapear).ToList());
            }
            catch (Exception ex) { return response.addError(ex); }
        }

        public async Task<ResponseDto<IEnumerable<PagamentoDto>>> ObterPorOrdemServicoIdAsync(int ordemServicoId)
        {
            var response = new ResponseDto<IEnumerable<PagamentoDto>>();
            try
            {
                var pagamentos = await _pagamentoService.ObterPorOrdemServicoIdAsync(ordemServicoId);
                return response.setResponse(pagamentos.Select(Mapear).ToList());
            }
            catch (Exception ex) { return response.addError(ex); }
        }

        public async Task<RespostaPagamento> SincronizarAsync(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                var pagamento = await _pagamentoService.SincronizarAsync(id, cancellationToken);

                return pagamento is null
                    ? RespostaPagamento.SemConteudo(ResultadoPagamento.SemMudanca)
                    : RespostaPagamento.De(ResultadoPagamento.Ok, Mapear(pagamento));
            }
            catch (KeyNotFoundException ex)
            {
                return RespostaPagamento.Erro(ResultadoPagamento.NaoEncontrado, ex.Message);
            }
            catch (GatewayPagamentoException ex)
            {
                return RespostaPagamento.Erro(ResultadoPagamento.FalhaProvedor, ex.Message);
            }
            catch (Exception ex)
            {
                return RespostaPagamento.Erro(ResultadoPagamento.FalhaProvedor, ex.Message);
            }
        }

        public async Task<RespostaPagamento> SincronizarPorIdExternoAsync(string idExterno, CancellationToken cancellationToken = default)
        {
            try
            {
                var pagamento = await _pagamentoService.SincronizarPorIdExternoAsync(idExterno, cancellationToken);

                // Nulo aqui cobre dois casos — pagamento desconhecido e nada mudou. Os dois
                // são "sem mudança" para o chamador: o webhook responde 200 de qualquer forma,
                // senão o provedor reenviaria para sempre.
                return pagamento is null
                    ? RespostaPagamento.SemConteudo(ResultadoPagamento.SemMudanca)
                    : RespostaPagamento.De(ResultadoPagamento.Ok, Mapear(pagamento));
            }
            catch (GatewayPagamentoException ex)
            {
                return RespostaPagamento.Erro(ResultadoPagamento.FalhaProvedor, ex.Message);
            }
            catch (Exception ex)
            {
                return RespostaPagamento.Erro(ResultadoPagamento.FalhaProvedor, ex.Message);
            }
        }

        private PagamentoDto Mapear(Pagamento pagamento)
        {
            var dto = _mapper.Map<PagamentoDto>(pagamento);
            dto.StatusDescricao = pagamento.Status.ToString();
            return dto;
        }
    }
}
