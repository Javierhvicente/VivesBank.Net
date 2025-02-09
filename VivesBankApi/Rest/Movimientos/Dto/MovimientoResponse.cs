using VivesBankApi.Rest.Movimientos.Models;
using VivesBankApi.utils.GuuidGenerator;

namespace VivesBankApi.Rest.Movimientos.Dto;

public class MovimientoResponse
{
    public string Guid { get; set; }
    
    public string ClienteGuid { get; set; }
        
    public DomiciliacionResponse? Domiciliacion { get; set; }
    
    public IngresoDeNomina? IngresoDeNomina { get; set; }
    
    public PagoConTarjeta? PagoConTarjeta { get; set; }
    
    public Transferencia? Transferencia { get; set; }

    public string? CreatedAt { get; set; }

    public string? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
}