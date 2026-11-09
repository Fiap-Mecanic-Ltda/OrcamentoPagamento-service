using OrcamentoPagamento.Application.DTOs;

namespace OrcamentoPagamento.Application.AppServices.Interfaces
{
    public interface IPagamentoAppService
    {
        Task<RespostaPagamento> CriarAsync(PagamentoCreateDto dto, CancellationToken cancellationToken = default);

        Task<ResponseDto<PagamentoDto>> ObterPorIdAsync(int id);

        Task<ResponseDto<IEnumerable<PagamentoDto>>> ObterPorOrcamentoIdAsync(int orcamentoId);

        Task<ResponseDto<IEnumerable<PagamentoDto>>> ObterPorOrdemServicoIdAsync(int ordemServicoId);

        /// <summary>
        /// Consulta o provedor e reaplica o status. Conteúdo nulo = nada mudou.
        /// </summary>
        Task<RespostaPagamento> SincronizarAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Mesma sincronização, a partir do id do provedor — é o que o webhook tem.
        /// </summary>
        Task<RespostaPagamento> SincronizarPorIdExternoAsync(string idExterno, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// O ResponseDto sozinho não distingue "não encontrado" de "conflito" de "provedor
    /// fora do ar", e o contrato publicado em docs/pagamento.md promete códigos HTTP
    /// diferentes para cada um. Este envelope carrega o desfecho junto do conteúdo.
    /// </summary>
    public class RespostaPagamento
    {
        public RespostaPagamento(ResultadoPagamento resultado, ResponseDto<PagamentoDto> conteudo)
        {
            Resultado = resultado;
            Conteudo = conteudo;
        }

        public ResultadoPagamento Resultado { get; }

        public ResponseDto<PagamentoDto> Conteudo { get; }

        public static RespostaPagamento De(ResultadoPagamento resultado, PagamentoDto dto)
            => new(resultado, new ResponseDto<PagamentoDto>().setResponse(dto));

        public static RespostaPagamento Erro(ResultadoPagamento resultado, string mensagem)
            => new(resultado, new ResponseDto<PagamentoDto>().addError(mensagem));

        public static RespostaPagamento SemConteudo(ResultadoPagamento resultado)
            => new(resultado, new ResponseDto<PagamentoDto>());
    }

    public enum ResultadoPagamento
    {
        /// <summary>Pagamento criado. 201.</summary>
        Criado,

        /// <summary>RequisicaoId repetido: devolve o existente sem cobrar de novo. 200.</summary>
        JaProcessado,

        /// <summary>Consulta ou sincronização com sucesso. 200.</summary>
        Ok,

        /// <summary>Sincronização sem mudança de estado. 204.</summary>
        SemMudanca,

        /// <summary>Orçamento ou pagamento inexistente. 404.</summary>
        NaoEncontrado,

        /// <summary>O orçamento já tem pagamento aprovado. 409.</summary>
        JaAprovado,

        /// <summary>Payload inválido ou orçamento sem valor. 400.</summary>
        DadosInvalidos,

        /// <summary>Falha de comunicação com o provedor. 502.</summary>
        FalhaProvedor
    }
}
