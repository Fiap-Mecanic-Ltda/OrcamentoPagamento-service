using Microsoft.EntityFrameworkCore;
using OrcamentoPagamento.Domain.Interfaces.Repositories.Base;

namespace OrcamentoPagamento.Infrastructure.Repositories.Base
{
    public abstract class Repository<TEntity> : IRepository<TEntity> where TEntity : class, new()
    {
        protected readonly BancoOrcamentoContext _context;
        protected readonly DbSet<TEntity> _dbSet;

        protected Repository(BancoOrcamentoContext context)
        {
            _context = context;
            _dbSet = context.Set<TEntity>();
        }

        public virtual async Task<TEntity> AdicionarAsync(TEntity entity)
        {
            var newEntity = _dbSet.Add(entity);
            await SaveChanges();
            return newEntity.Entity;
        }

        /// <summary>
        /// Atualiza a entidade. Quando ela já está rastreada pelo contexto — caso do
        /// orçamento carregado com seus itens — basta salvar: chamar Update() num grafo
        /// rastreado reescreveria o estado dos filhos que o domínio acabou de remover.
        /// </summary>
        public virtual async Task<TEntity> AtualizarAsync(TEntity entity)
        {
            var entry = _context.Entry(entity);

            if (entry.State == EntityState.Detached)
                _dbSet.Update(entity);

            await SaveChanges();
            return entity;
        }

        public virtual async Task RemoverAsync(string id)
        {
            var entity = await ObterPorIdAsync(id);

            if (entity is not null)
            {
                _dbSet.Remove(entity);
                await SaveChanges();
            }
        }

        public virtual async Task<IEnumerable<TEntity>> ObterTodosAsync()
        {
            return await _dbSet.AsNoTracking().ToListAsync();
        }

        public virtual async Task<TEntity?> ObterPorIdAsync(string id)
        {
            // Converte para o tipo correto da PK — FindAsync falha silenciosamente
            // quando a PK é int e recebe uma string.
            var keyType = _context.Model
                .FindEntityType(typeof(TEntity))!
                .FindPrimaryKey()!
                .Properties[0]
                .ClrType;

            var convertedId = Convert.ChangeType(id, keyType);
            return await _dbSet.FindAsync(convertedId);
        }

        public async Task<int> SaveChanges()
        {
            return await _context.SaveChangesAsync();
        }

        public void Dispose()
        {
            _context.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
