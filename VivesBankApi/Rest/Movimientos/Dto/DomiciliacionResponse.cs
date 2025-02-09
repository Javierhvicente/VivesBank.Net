using VivesBankApi.Rest.Movimientos.Models;
using VivesBankApi.utils.GuuidGenerator;

namespace VivesBankApi.Rest.Movimientos.Dto;

public class DomiciliacionResponse
{
    public string Guid { get; set; }
    
    public string ClienteGuid { get; set; }
    
    public string IbanOrigen { get; set; }
    
    public string IbanDestino { get; set; }

    public decimal Cantidad { get; set; }

    public string NombreAcreedor { get; set; }

    public string FechaInicio { get; set; }

    public string Periodicidad { get; set; }

    public bool Activa { get; set; }

    public string UltimaEjecucion { get; set; }
}