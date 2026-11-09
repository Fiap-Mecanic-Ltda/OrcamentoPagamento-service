namespace OrcamentoPagamento.API.Authorization
{
    /// <summary>
    /// Mesmas roles do monólito — os tokens são os mesmos, emitidos fora deste serviço.
    /// </summary>
    public static class Roles
    {
        public const string Administrador = "Administrador";
        public const string Funcionario = "Funcionario";
        public const string Cliente = "Cliente";

        /// <summary>Roles com acesso administrativo completo.</summary>
        public const string Admin = $"{Administrador},{Funcionario}";

        /// <summary>Roles que podem consultar o orçamento da própria OS.</summary>
        public const string AdminOuCliente = $"{Administrador},{Funcionario},{Cliente}";
    }
}
