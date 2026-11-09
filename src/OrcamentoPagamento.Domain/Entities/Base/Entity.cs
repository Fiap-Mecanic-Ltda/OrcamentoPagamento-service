using System.ComponentModel.DataAnnotations;

namespace OrcamentoPagamento.Domain.Entities.Base
{
    public class Entity : Entity<int>
    {
    }

    public class Entity<T>
    {
        [Key]
        public T Id { get; set; } = default!;
    }
}
