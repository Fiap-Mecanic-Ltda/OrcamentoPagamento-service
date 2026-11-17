using OrcamentoPagamento.Application.DTOs;

namespace OrcamentoPagamento.Application.AppServices.Interfaces
{
    public interface IOrcamentoPdfAppService
    {
        byte[] Gerar(OrcamentoPdfDto dto);
    }
}
