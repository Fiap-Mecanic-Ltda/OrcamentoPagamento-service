using AutoMapper;
using OrcamentoPagamento.Application.DTOs;
using OrcamentoPagamento.Domain.Entities;

namespace OrcamentoPagamento.Application.Mapping
{
    public class OrcamentoProfile : Profile
    {
        public OrcamentoProfile()
        {
            CreateMap<Orcamento, OrcamentoDto>();
            CreateMap<ItemOrcamento, ItemOrcamentoDto>();

            // StatusDescricao e preenchido no AppService, a partir do enum.
            CreateMap<Pagamento, PagamentoDto>()
                .ForMember(d => d.StatusDescricao, o => o.Ignore());
        }
    }
}
