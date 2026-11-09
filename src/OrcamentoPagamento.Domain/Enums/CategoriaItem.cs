namespace OrcamentoPagamento.Domain.Enums
{
    /// <summary>
    /// Classificação do item para efeito de totalização do orçamento.
    /// Chega pronta no evento publicado pela Ordem de Serviço: é lá que se sabe se o
    /// item consumiu estoque (Peça/Insumo, conforme TipoEstoque) ou se é mão de obra.
    /// </summary>
    public enum CategoriaItem
    {
        Peca = 1,
        Insumo = 2,
        Servico = 3
    }
}
