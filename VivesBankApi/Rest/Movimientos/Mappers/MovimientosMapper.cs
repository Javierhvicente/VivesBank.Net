using VivesBankApi.Rest.Movimientos.Dto;
using VivesBankApi.Rest.Movimientos.Models;

namespace VivesBankApi.Rest.Movimientos.Mappers;

public static class MovimientosMapper
{
    public static DomiciliacionResponse ToDomiciliacionResponseFromModel(this Domiciliacion domiciliacion)
    {
        return new DomiciliacionResponse()
        {
            Guid = domiciliacion.Guid,
            ClienteGuid = domiciliacion.ClienteGuid,
            IbanOrigen = domiciliacion.IbanOrigen,
            IbanDestino = domiciliacion.IbanDestino,
            Cantidad = domiciliacion.Cantidad,
            NombreAcreedor = domiciliacion.NombreAcreedor,
            FechaInicio = domiciliacion.FechaInicio.ToString(),
            Periodicidad = domiciliacion.Periodicidad.ToString(),
            Activa = domiciliacion.Activa,
            UltimaEjecucion = domiciliacion.UltimaEjecucion.ToString()
        };
    }
    
    public static MovimientoResponse ToMovimientoResponseFromModel(this Movimiento movimientoResponse)
    {
        return new MovimientoResponse()
        {
            Guid = movimientoResponse.Guid,
            ClienteGuid = movimientoResponse.ClienteGuid,
            Domiciliacion = movimientoResponse.Domiciliacion?.ToDomiciliacionResponseFromModel(),
            IngresoDeNomina = movimientoResponse.IngresoDeNomina,
            PagoConTarjeta = movimientoResponse.PagoConTarjeta,
            Transferencia = movimientoResponse.Transferencia,
            CreatedAt = movimientoResponse.CreatedAt.ToString(),
            UpdatedAt = movimientoResponse.UpdatedAt.ToString(),
            IsDeleted = movimientoResponse.IsDeleted,
        };
    }
}