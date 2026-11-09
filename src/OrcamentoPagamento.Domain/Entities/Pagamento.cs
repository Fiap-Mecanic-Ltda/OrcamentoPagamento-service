using OrcamentoPagamento.Domain.Entities.Base;
using OrcamentoPagamento.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OrcamentoPagamento.Domain.Entities
{
    /// <summary>
    /// Agregado raiz do módulo de pagamento. Vive no mesmo serviço do orçamento, mas é
    /// agregado próprio: nada aqui é alterado pelo cálculo do orçamento, exceto o
    /// cancelamento de pendente quando o valor muda.
    /// </summary>
    public class Pagamento : Entity<int>
    {
        public const string ProvedorMercadoPago = "MercadoPago";

        /// <summary>
        /// Identificador da tentativa, gerado pelo Web. Índice único: é a idempotência de
        /// entrada — reenvio do mesmo valor devolve este pagamento em vez de cobrar de novo.
        /// </summary>
        [Required]
        public Guid RequisicaoId { get; set; }

        [Required]
        public int OrcamentoId { get; set; }

        [ForeignKey(nameof(OrcamentoId))]
        public Orcamento Orcamento { get; set; } = null!;

        /// <summary>Copiado do orçamento, para consulta direta sem join.</summary>
        [Required]
        public int OrdemServicoId { get; set; }

        /// <summary>
        /// Valor congelado no momento da criação, lido do orçamento. Nunca vem da
        /// requisição do Web — senão daria para pagar R$ 0,01 por um orçamento de R$ 450.
        /// </summary>
        [Required]
        public decimal Valor { get; set; }

        [Required]
        public StatusPagamento Status { get; set; }

        /// <summary>`status_detail` do provedor, cru, para diagnóstico.</summary>
        [MaxLength(100)]
        public string? StatusDetalhe { get; set; }

        [Required]
        [MaxLength(30)]
        public string Provedor { get; set; } = ProvedorMercadoPago;

        /// <summary>Id do pagamento no provedor. Nulo enquanto a criação não retornou.</summary>
        [MaxLength(50)]
        public string? IdExterno { get; set; }

        [Required]
        public int Parcelas { get; set; } = 1;

        /// <summary>`payment_method_id` do provedor: master, visa, amex, elo, pix…</summary>
        [MaxLength(30)]
        public string? MetodoPagamento { get; set; }

        /// <summary>
        /// Espelha o `live_mode` do provedor. Separa teste de produção no mesmo banco —
        /// enquanto só rodamos com credenciais de teste, é sempre false.
        /// </summary>
        public bool ModoTeste { get; set; }

        /// <summary>
        /// `date_last_updated` do provedor. É o relógio da ordenação: notificação com valor
        /// anterior a este é atrasada e deve ser descartada.
        /// </summary>
        public DateTime? UltimaAtualizacaoProvedor { get; set; }

        /// <summary>Preenchido quando o cancelamento é nosso, não do provedor.</summary>
        [MaxLength(50)]
        public string? MotivoCancelamento { get; set; }

        [Required]
        public DateTime CriadoEm { get; set; }

        [Required]
        public DateTime AtualizadoEm { get; set; }

        /// <summary>Status em que o pagamento ainda pode ser concluído ou abandonado.</summary>
        public bool EstaEmAberto()
            => Status is StatusPagamento.Pendente or StatusPagamento.EmProcessamento or StatusPagamento.Autorizado;

        /// <summary>
        /// Notificações do provedor chegam fora de ordem e repetidas. Descarta a que for
        /// anterior à última aplicada. Mesmo raciocínio do OcorridoEm no evento de itens
        /// da OS, com outro relógio.
        /// </summary>
        public bool DeveDescartarAtualizacao(DateTime? ultimaAtualizacaoProvedor)
        {
            if (!ultimaAtualizacaoProvedor.HasValue || !UltimaAtualizacaoProvedor.HasValue)
                return false;

            return ultimaAtualizacaoProvedor.Value < UltimaAtualizacaoProvedor.Value;
        }

        /// <summary>
        /// Máquina de estados. Recusado e Cancelado são terminais; de Aprovado só se sai
        /// para os estados de devolução. Sem isso, uma notificação atrasada de "pending"
        /// faria um pagamento aprovado voltar a pendente.
        /// </summary>
        public bool PodeTransicionarPara(StatusPagamento novo)
        {
            if (novo == Status)
                return false;

            return Status switch
            {
                StatusPagamento.Pendente or StatusPagamento.EmProcessamento or StatusPagamento.Autorizado => true,

                StatusPagamento.Aprovado =>
                    novo is StatusPagamento.Devolvido or StatusPagamento.EmDisputa or StatusPagamento.Estornado,

                StatusPagamento.EmDisputa =>
                    novo is StatusPagamento.Devolvido or StatusPagamento.Estornado or StatusPagamento.Aprovado,

                StatusPagamento.Devolvido => novo is StatusPagamento.Estornado,

                // Recusado, Cancelado e Estornado são terminais.
                _ => false
            };
        }

        /// <summary>
        /// Aplica o estado vindo do provedor. Retorna false quando nada muda — seja por
        /// atualização atrasada, seja por transição não permitida.
        /// </summary>
        public bool AplicarStatusDoProvedor(
            StatusPagamento novoStatus,
            string? statusDetalhe,
            string? idExterno,
            string? metodoPagamento,
            int? parcelas,
            bool modoTeste,
            DateTime? ultimaAtualizacaoProvedor)
        {
            if (DeveDescartarAtualizacao(ultimaAtualizacaoProvedor))
                return false;

            if (!PodeTransicionarPara(novoStatus))
                return false;

            Status = novoStatus;
            StatusDetalhe = statusDetalhe;
            ModoTeste = modoTeste;

            if (!string.IsNullOrWhiteSpace(idExterno))
                IdExterno = idExterno;

            if (!string.IsNullOrWhiteSpace(metodoPagamento))
                MetodoPagamento = metodoPagamento;

            if (parcelas.HasValue && parcelas.Value > 0)
                Parcelas = parcelas.Value;

            if (ultimaAtualizacaoProvedor.HasValue)
                UltimaAtualizacaoProvedor = ultimaAtualizacaoProvedor;

            AtualizadoEm = DateTime.UtcNow;
            return true;
        }

        /// <summary>
        /// Cancelamento originado aqui. Só vale para pagamento em aberto: o que já foi
        /// aprovado não se cancela, se estorna — e isso é decisão humana.
        /// </summary>
        public bool Cancelar(string motivo)
        {
            if (!EstaEmAberto())
                return false;

            Status = StatusPagamento.Cancelado;
            MotivoCancelamento = motivo;
            AtualizadoEm = DateTime.UtcNow;
            return true;
        }
    }
}
