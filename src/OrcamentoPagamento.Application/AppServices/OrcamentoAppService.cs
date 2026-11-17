using AutoMapper;
using OrcamentoPagamento.Application.AppServices.Interfaces;
using OrcamentoPagamento.Application.DTOs;
using OrcamentoPagamento.Application.Eventos;
using OrcamentoPagamento.Domain.Commands;
using OrcamentoPagamento.Domain.Entities;
using OrcamentoPagamento.Domain.Exceptions;
using OrcamentoPagamento.Domain.Interfaces.Services;

namespace OrcamentoPagamento.Application.AppServices
{
    public class OrcamentoAppService : IOrcamentoAppService
    {
        private readonly IOrcamentoService _orcamentoService;
        private readonly IOrcamentoPdfAppService _pdfService;
        private readonly ISagaOrcamentoService _sagaService;
        private readonly IMapper _mapper;

        public OrcamentoAppService(
            IOrcamentoService orcamentoService,
            IOrcamentoPdfAppService pdfService,
            ISagaOrcamentoService sagaService,
            IMapper mapper)
        {
            _sagaService = sagaService;
            _orcamentoService = orcamentoService;
            _pdfService = pdfService;
            _mapper = mapper;
        }

        public async Task<ResponseDto<OrcamentoDto>> ObterPorIdAsync(string id)
        {
            var response = new ResponseDto<OrcamentoDto>();
            try
            {
                var orcamento = await _orcamentoService.ObterPorIdAsync(id)
                    ?? throw new KeyNotFoundException($"Orcamento com Id {id} nao encontrado.");

                return response.setResponse(_mapper.Map<OrcamentoDto>(orcamento));
            }
            catch (KeyNotFoundException ex) { return response.addError(ex.Message); }
            catch (Exception ex) { return response.addError(ex); }
        }

        public async Task<ResponseDto<OrcamentoDto>> ObterPorOrdemServicoIdAsync(int ordemServicoId)
        {
            var response = new ResponseDto<OrcamentoDto>();
            try
            {
                var orcamento = await _orcamentoService.ObterPorOrdemServicoIdAsync(ordemServicoId)
                    ?? throw new KeyNotFoundException($"Nenhum orcamento encontrado para a OS {ordemServicoId}.");

                return response.setResponse(_mapper.Map<OrcamentoDto>(orcamento));
            }
            catch (KeyNotFoundException ex) { return response.addError(ex.Message); }
            catch (Exception ex) { return response.addError(ex); }
        }

        public async Task<ResponseDto<OrcamentoDto>> AtualizarManualAsync(string id, OrcamentoUpdateDto dto, CancellationToken cancellationToken = default)
        {
            var response = new ResponseDto<OrcamentoDto>();
            try
            {
                if (!int.TryParse(id, out var orcamentoId))
                    throw new ArgumentException($"Id de orcamento invalido: {id}.");

                var resultado = await _orcamentoService.AtualizarManualAsync(
                    orcamentoId,
                    dto.ValorTotalPecas,
                    dto.ValorTotalInsumos,
                    dto.ValorTotalServicos,
                    dto.Validade,
                    cancellationToken);

                return response.setResponse(_mapper.Map<OrcamentoDto>(resultado));
            }
            catch (KeyNotFoundException ex) { return response.addError(ex.Message); }
            catch (Exception ex) { return response.addError(ex); }
        }

        public async Task<ResponseDto<bool>> RemoverAsync(string id, CancellationToken cancellationToken = default)
        {
            var response = new ResponseDto<bool>();
            try
            {
                await _orcamentoService.RemoverAsync(id, cancellationToken);
                return response.setResponse(true);
            }
            catch (KeyNotFoundException ex) { return response.addError(ex.Message); }
            catch (Exception ex) { return response.addError(ex); }
        }

        public async Task<ResponseDto<OrcamentoDto?>> RecalcularAsync(OrdemServicoItensAlteradosEvent evento, CancellationToken cancellationToken = default)
        {
            var response = new ResponseDto<OrcamentoDto?>();
            try
            {
                var command = new CalcularOrcamentoCommand
                {
                    EventoId = evento.EventoId == Guid.Empty ? Guid.NewGuid() : evento.EventoId,
                    OcorridoEm = evento.OcorridoEm == default ? DateTime.UtcNow : evento.OcorridoEm,
                    OrdemServicoId = evento.OrdemServicoId,
                    Itens = evento.Itens.Select(i => new ItemCalculoCommand
                    {
                        ItemOrigemId = i.ItemId,
                        Descricao = i.Descricao,
                        Categoria = i.Categoria,
                        Quantidade = i.Quantidade,
                        ValorUnitario = i.ValorUnitario
                    }).ToList(),
                    OrdemServico = evento.OrdemServico is null ? null : new OrdemServicoDadosCommand
                    {
                        DescricaoProblema = evento.OrdemServico.DescricaoProblema,
                        Status = evento.OrdemServico.Status,
                        ClienteNome = evento.OrdemServico.ClienteNome,
                        ClienteEmail = evento.OrdemServico.ClienteEmail,
                        ClienteTelefone = evento.OrdemServico.ClienteTelefone,
                        VeiculoMarca = evento.OrdemServico.VeiculoMarca,
                        VeiculoModelo = evento.OrdemServico.VeiculoModelo,
                        VeiculoPlaca = evento.OrdemServico.VeiculoPlaca,
                        VeiculoAno = evento.OrdemServico.VeiculoAno
                    }
                };

                var orcamento = await _orcamentoService.CalcularAsync(command, cancellationToken);

                // null = pedido descartado por idempotência (eventoId repetido). Não é erro:
                // a rota responde 204.
                return response.setResponse(orcamento is null ? null : _mapper.Map<OrcamentoDto>(orcamento));
            }
            catch (Exception ex) { return response.addError(ex); }
        }

        public Task<(ResultadoDecisaoOrcamento Resultado, ResponseDto<OrcamentoDto> Resposta)> AprovarAsync(int id, CancellationToken cancellationToken = default)
            => DecidirAsync(() => _sagaService.AprovarAsync(id, cancellationToken));

        public Task<(ResultadoDecisaoOrcamento Resultado, ResponseDto<OrcamentoDto> Resposta)> RecusarAsync(int id, string? motivo, CancellationToken cancellationToken = default)
            => DecidirAsync(() => _sagaService.RecusarAsync(id, motivo, cancellationToken));

        private async Task<(ResultadoDecisaoOrcamento, ResponseDto<OrcamentoDto>)> DecidirAsync(Func<Task<Orcamento>> acao)
        {
            var response = new ResponseDto<OrcamentoDto>();
            try
            {
                var orcamento = await acao();
                return (ResultadoDecisaoOrcamento.Ok, response.setResponse(_mapper.Map<OrcamentoDto>(orcamento)));
            }
            catch (KeyNotFoundException ex) { return (ResultadoDecisaoOrcamento.NaoEncontrado, response.addError(ex.Message)); }
            catch (OrcamentoIndisponivelException ex) { return (ResultadoDecisaoOrcamento.Indisponivel, response.addError(ex.Message)); }
            catch (Exception ex) { return (ResultadoDecisaoOrcamento.Erro, response.addError(ex)); }
        }

        public async Task<ResponseDto<byte[]>> ExportarPdfAsync(int id)
        {
            var response = new ResponseDto<byte[]>();
            try
            {
                var orcamento = await _orcamentoService.ObterComDetalhesAsync(id)
                    ?? throw new KeyNotFoundException($"Orcamento com Id {id} nao encontrado.");

                var snapshot = orcamento.OrdemServicoSnapshot;

                var dto = new OrcamentoPdfDto
                {
                    Id = orcamento.Id,
                    OrdemServicoId = orcamento.OrdemServicoId,
                    DataGeracao = orcamento.DataGeracao,
                    Validade = orcamento.Validade,
                    ValorTotalPecas = orcamento.ValorTotalPecas,
                    ValorTotalInsumos = orcamento.ValorTotalInsumos,
                    ValorTotalServicos = orcamento.ValorTotalServicos,
                    ValorTotalGeral = orcamento.ValorTotalGeral,
                    DescricaoProblema = snapshot?.DescricaoProblema,
                    StatusOrdemServico = snapshot?.StatusOrdemServico,
                    ClienteNome = snapshot?.ClienteNome,
                    ClienteEmail = snapshot?.ClienteEmail,
                    ClienteTelefone = snapshot?.ClienteTelefone,
                    VeiculoMarca = snapshot?.VeiculoMarca,
                    VeiculoModelo = snapshot?.VeiculoModelo,
                    VeiculoPlaca = snapshot?.VeiculoPlaca,
                    VeiculoAno = snapshot?.VeiculoAno,
                    Itens = _mapper.Map<List<ItemOrcamentoDto>>(orcamento.Itens)
                };

                var bytes = _pdfService.Gerar(dto);
                return response.setResponse(bytes);
            }
            catch (KeyNotFoundException ex) { return response.addError(ex.Message); }
            catch (Exception ex) { return response.addError(ex); }
        }
    }
}
