namespace OrcamentoPagamento.Domain.Enums
{
    /// <summary>
    /// Espelha 1:1 os status do provedor, para não perder informação na tradução.
    /// O mapeamento do vocabulário do provedor para cá é responsabilidade do adapter
    /// (ver MercadoPagoGateway) — o domínio não conhece as strings do Mercado Pago.
    /// </summary>
    public enum StatusPagamento
    {
        Pendente = 1,
        EmProcessamento = 2,
        Autorizado = 3,
        Aprovado = 4,
        Recusado = 5,
        Cancelado = 6,
        Devolvido = 7,
        EmDisputa = 8,
        Estornado = 9
    }
}
