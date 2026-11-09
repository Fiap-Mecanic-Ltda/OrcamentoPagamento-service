using OrcamentoPagamento.Domain.Commands;
using OrcamentoPagamento.Domain.Entities;

namespace OrcamentoPagamento.Domain.Interfaces.Services
{
    public interface IPagamentoService
    {
        /// <summary>
        /// Cria a cobrança. <see cref="ResultadoCriacaoPagamento.JaExistia"/> indica que o
        /// RequisicaoId já havia sido usado e nada foi cobrado de novo.
        /// </summary>
        Task<ResultadoCriacaoPagamento> CriarAsync(CriarPagamentoCommand command, CancellationToken cancellationToken = default);

        Task<Pagamento?> ObterPorIdAsync(int id);

        Task<IEnumerable<Pagamento>> ObterPorOrcamentoIdAsync(int orcamentoId);

        Task<IEnumerable<Pagamento>> ObterPorOrdemServicoIdAsync(int ordemServicoId);

        /// <summary>
        /// Consulta o provedor e reaplica o status. Usado pelo webhook e pela rota de
        /// sincronização. Retorna null quando nada mudou.
        /// </summary>
        Task<Pagamento?> SincronizarAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Igual ao <see cref="SincronizarAsync"/>, mas a partir do id do provedor — é o que
        /// o webhook tem em mãos. Retorna null quando não há pagamento conhecido com esse
        /// id, ou quando nada mudou.
        /// </summary>
        Task<Pagamento?> SincronizarPorIdExternoAsync(string idExterno, CancellationToken cancellationToken = default);

        /// <summary>
        /// Cancela os pagamentos em aberto do orçamento. Chamado pelo recálculo quando o
        /// valor muda. Devolve quantos foram cancelados.
        /// </summary>
        Task<int> CancelarEmAbertoAsync(int orcamentoId, string motivo, CancellationToken cancellationToken = default);
    }

    public class ResultadoCriacaoPagamento
    {
        public ResultadoCriacaoPagamento(Pagamento pagamento, bool jaExistia)
        {
            Pagamento = pagamento;
            JaExistia = jaExistia;
        }

        public Pagamento Pagamento { get; }

        /// <summary>true quando o RequisicaoId era repetido: nada foi cobrado de novo.</summary>
        public bool JaExistia { get; }
    }
}
