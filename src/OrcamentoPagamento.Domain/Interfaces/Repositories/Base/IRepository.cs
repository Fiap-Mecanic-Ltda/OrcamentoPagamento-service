namespace OrcamentoPagamento.Domain.Interfaces.Repositories.Base
{
    public interface IRepository<TEntity> : IDisposable where TEntity : class
    {
        Task<TEntity> AdicionarAsync(TEntity entity);
        Task<TEntity> AtualizarAsync(TEntity entity);
        Task RemoverAsync(string id);
        Task<IEnumerable<TEntity>> ObterTodosAsync();
        Task<TEntity?> ObterPorIdAsync(string id);
        Task<int> SaveChanges();
    }
}
