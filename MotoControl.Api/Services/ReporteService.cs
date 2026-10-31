using MotoControl.Api.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace MotoControl.Api.Services;

/// <summary>Generación de los mismos reportes PDF de la app WPF, ahora en el servidor.</summary>
public sealed class ReporteService
{
    public static string NombreArchivoSeguro(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "sin_dato";
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(valor.Where(ch => !invalidos.Contains(ch)).ToArray());
        return limpio.Replace(' ', '_');
    }

    private static string Money(Asociacion a, decimal valor) => $"{a.Moneda}{valor:N2}";

    private static void Encabezado(IContainer container, Asociacion a, string titulo, string? subtitulo = null)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                var tieneLogo = a.Logo is { Length: > 0 };

                if (tieneLogo)
                    row.ConstantItem(70).Height(70).Image(a.Logo!).FitArea();

                row.RelativeItem().PaddingLeft(tieneLogo ? 12 : 0).Column(c =>
                {
                    c.Item().Text(a.Nombre).FontSize(16).Bold().FontColor(Colors.Blue.Darken3);
                    c.Item().Text(titulo).FontSize(12).SemiBold();

                    if (!string.IsNullOrWhiteSpace(subtitulo))
                        c.Item().Text(subtitulo).FontSize(10).FontColor(Colors.Grey.Darken2);

                    c.Item().Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}")
                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });

            col.Item().PaddingVertical(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
        });
    }

    private static void Pie(IContainer container)
    {
        container.AlignCenter().Text(text =>
        {
            text.Span("Página ").FontSize(8);
            text.CurrentPageNumber().FontSize(8);
            text.Span(" de ").FontSize(8);
            text.TotalPages().FontSize(8);
        });
    }

    private static byte[] Generar(Action<IDocumentContainer> construir)
        => Document.Create(construir).GeneratePdf();

    // ==================================================================
    //  RECIBO DE PAGO
    // ==================================================================
    public byte[] Recibo(Asociacion a, string numeroSocio, string nombreSocio, PagoDto pago) =>
        Generar(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Element(c => Encabezado(c, a, "Recibo de Pago", $"N° Socio: {numeroSocio}"));

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().AlignCenter().Text("RECIBO DE PAGO")
                        .FontSize(16).Bold().FontColor(Colors.Blue.Darken3);

                    col.Item().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(12).Column(c =>
                    {
                        c.Spacing(6);
                        Fila(c, "Socio:", $"{nombreSocio} ({numeroSocio})");
                        Fila(c, "Período:", pago.Etiqueta);
                        Fila(c, "Vencimiento:", Vencimiento(pago).ToString("dd/MM/yyyy"));
                        Fila(c, "Fecha de pago:", pago.FechaPago.ToString("dd/MM/yyyy HH:mm"));
                        c.Item().Row(r =>
                        {
                            r.ConstantItem(110).Text("Monto:").SemiBold();
                            r.RelativeItem().Text(Money(a, pago.Monto))
                                .FontColor(Colors.Green.Darken3).Bold();
                        });
                        if (!string.IsNullOrWhiteSpace(pago.Observacion))
                            Fila(c, "Observación:", pago.Observacion);
                    });

                    col.Item().PaddingTop(30).Column(c =>
                    {
                        c.Item().LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                        c.Item().AlignCenter().Text("Firma / Sello")
                            .FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Comprobante generado por Moto Control · ").FontSize(8)
                        .FontColor(Colors.Grey.Darken1);
                    text.Span(DateTime.Now.ToString("dd/MM/yyyy HH:mm")).FontSize(8)
                        .FontColor(Colors.Grey.Darken1);
                });
            });
        });

    private static void Fila(ColumnDescriptor c, string etiqueta, string valor)
        => c.Item().Row(r =>
        {
            r.ConstantItem(110).Text(etiqueta).SemiBold();
            r.RelativeItem().Text(valor);
        });

    private static DateOnly Vencimiento(PagoDto p) => p.Quincena == 1
        ? new DateOnly(p.Anio, p.Mes, 15)
        : new DateOnly(p.Anio, p.Mes, DateTime.DaysInMonth(p.Anio, p.Mes));

    // ==================================================================
    //  BALANCE TOTAL
    // ==================================================================
    public byte[] BalanceTotal(Asociacion a, List<PagoDto> pagos) =>
        Generar(container =>
        {
            var agrupado = pagos.GroupBy(p => p.NumeroSocio).OrderBy(g => g.Key).ToList();
            var totalRecaudado = pagos.Sum(p => p.Monto);

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Element(c => Encabezado(c, a, "Balance Total de Recaudación"));
                page.Footer().Element(Pie);

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Background(Colors.Blue.Lighten4).Padding(10).Column(c =>
                    {
                        c.Item().Text($"Total recaudado: {Money(a, totalRecaudado)}")
                            .FontSize(13).Bold().FontColor(Colors.Blue.Darken3);
                        c.Item().Text($"Socios con pagos: {agrupado.Count}").FontSize(10);
                        c.Item().Text($"Cuotas registradas: {pagos.Count}").FontSize(10);
                    });

                    col.Item().Text("Resumen por Socio").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(70);
                            columns.RelativeColumn();
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(90);
                        });

                        table.Header(header =>
                        {
                            CeldaTitulo(header, "N° Socio");
                            CeldaTitulo(header, "Nombre");
                            CeldaTitulo(header, "Cuotas", true);
                            CeldaTitulo(header, "Total", true);
                        });

                        foreach (var g in agrupado)
                        {
                            var nombre = g.First().NombreSocio;
                            table.Cell().Padding(3).Text(g.Key);
                            table.Cell().Padding(3).Text(string.IsNullOrWhiteSpace(nombre) ? "—" : nombre);
                            table.Cell().Padding(3).AlignRight().Text(g.Count().ToString());
                            table.Cell().Padding(3).AlignRight().Text(Money(a, g.Sum(p => p.Monto)));
                        }
                    });

                    col.Item().PaddingTop(10).Text("Detalle de Pagos").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(110);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            CeldaTitulo(header, "Socio");
                            CeldaTitulo(header, "Período");
                            CeldaTitulo(header, "Vencimiento");
                            CeldaTitulo(header, "Monto", true);
                            CeldaTitulo(header, "Fecha pago");
                            CeldaTitulo(header, "Observación");
                        });

                        foreach (var p in pagos.OrderBy(x => x.Anio).ThenBy(x => x.Mes)
                                     .ThenBy(x => x.Quincena).ThenBy(x => x.NumeroSocio))
                        {
                            table.Cell().Padding(3).Text(p.NumeroSocio);
                            table.Cell().Padding(3).Text(p.Etiqueta);
                            table.Cell().Padding(3).Text(Vencimiento(p).ToString("dd/MM/yyyy"));
                            table.Cell().Padding(3).AlignRight().Text(Money(a, p.Monto));
                            table.Cell().Padding(3).Text(p.FechaPago.ToString("dd/MM/yyyy"));
                            table.Cell().Padding(3).Text(p.Observacion);
                        }
                    });
                });
            });
        });

    private static void CeldaTitulo(TableCellDescriptor header, string texto, bool derecha = false)
    {
        var cell = header.Cell().Background(Colors.Grey.Lighten2).Padding(4);
        if (derecha) cell = cell.AlignRight();
        cell.Text(texto).Bold();
    }

    // ==================================================================
    //  BALANCE POR SOCIO
    // ==================================================================
    public byte[] BalancePorSocio(Asociacion a, string numeroSocio, string nombreSocio, List<PagoDto> pagos) =>
        Generar(container =>
        {
            var total = pagos.Sum(p => p.Monto);

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Element(c => Encabezado(c, a,
                    $"Balance del Socio: {nombreSocio}", $"N° Socio: {numeroSocio}"));
                page.Footer().Element(Pie);

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Background(Colors.Blue.Lighten4).Padding(10).Column(c =>
                    {
                        c.Item().Text($"Total pagado: {Money(a, total)}")
                            .FontSize(13).Bold().FontColor(Colors.Blue.Darken3);
                        c.Item().Text($"Cuotas registradas: {pagos.Count}").FontSize(10);
                    });

                    col.Item().Text("Detalle de Cuotas").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(110);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            CeldaTitulo(header, "Período");
                            CeldaTitulo(header, "Vencimiento");
                            CeldaTitulo(header, "Monto", true);
                            CeldaTitulo(header, "Fecha pago");
                            CeldaTitulo(header, "Observación");
                        });

                        foreach (var p in pagos.OrderBy(x => x.Anio).ThenBy(x => x.Mes).ThenBy(x => x.Quincena))
                        {
                            table.Cell().Padding(3).Text(p.Etiqueta);
                            table.Cell().Padding(3).Text(Vencimiento(p).ToString("dd/MM/yyyy"));
                            table.Cell().Padding(3).AlignRight().Text(Money(a, p.Monto));
                            table.Cell().Padding(3).Text(p.FechaPago.ToString("dd/MM/yyyy"));
                            table.Cell().Padding(3).Text(p.Observacion);
                        }
                    });

                    col.Item().PaddingTop(6).AlignRight()
                        .Text($"TOTAL: {Money(a, total)}")
                        .FontSize(12).Bold().FontColor(Colors.Blue.Darken3);
                });
            });
        });

    // ==================================================================
    //  RECAUDOS
    // ==================================================================
    public byte[] Recaudos(Asociacion a, RecaudoListadoDto data, DateOnly? desde, DateOnly? hasta) =>
        Generar(container =>
        {
            string rango = desde.HasValue && hasta.HasValue
                ? $"Del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}"
                : desde.HasValue ? $"Desde {desde:dd/MM/yyyy}"
                : hasta.HasValue ? $"Hasta {hasta:dd/MM/yyyy}"
                : "Todos los movimientos";

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Element(c => Encabezado(c, a, "Reporte de Recaudos", rango));
                page.Footer().Element(Pie);

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Background(Colors.Green.Lighten4).Padding(8).Column(c =>
                        {
                            c.Item().Text("Total ingresos").FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(Money(a, data.TotalIngresos)).FontSize(14).Bold()
                                .FontColor(Colors.Green.Darken3);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Background(Colors.Red.Lighten4).Padding(8).Column(c =>
                        {
                            c.Item().Text("Total egresos").FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(Money(a, data.TotalEgresos)).FontSize(14).Bold()
                                .FontColor(Colors.Red.Darken3);
                        });
                        row.ConstantItem(8);
                        row.RelativeItem().Background(Colors.Blue.Lighten4).Padding(8).Column(c =>
                        {
                            c.Item().Text("Balance neto").FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(Money(a, data.Balance)).FontSize(14).Bold()
                                .FontColor(Colors.Blue.Darken3);
                        });
                    });

                    col.Item().PaddingTop(6).Text("Detalle de Movimientos").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(70);
                            columns.ConstantColumn(70);
                            columns.ConstantColumn(120);
                            columns.RelativeColumn();
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(80);
                        });

                        table.Header(header =>
                        {
                            CeldaTitulo(header, "Fecha");
                            CeldaTitulo(header, "Tipo");
                            CeldaTitulo(header, "Concepto");
                            CeldaTitulo(header, "Descripción");
                            CeldaTitulo(header, "Socio");
                            CeldaTitulo(header, "Monto", true);
                        });

                        foreach (var r in data.Items.OrderBy(x => x.Fecha))
                        {
                            var color = r.Tipo == 0 ? Colors.Green.Darken2 : Colors.Red.Darken2;
                            table.Cell().Padding(3).Text(r.Fecha.ToString("dd/MM/yyyy"));
                            table.Cell().Padding(3).Text(r.Tipo == 0 ? "Ingreso" : "Egreso")
                                .FontColor(color).SemiBold();
                            table.Cell().Padding(3).Text(r.Concepto);
                            table.Cell().Padding(3).Text(r.Descripcion);
                            table.Cell().Padding(3).Text(
                                string.IsNullOrWhiteSpace(r.NumeroSocio) ? "—" : r.NumeroSocio);
                            table.Cell().Padding(3).AlignRight().Text(Money(a, r.Monto)).FontColor(color);
                        }
                    });

                    Totales(col, a, data);
                });
            });
        });

    private static void Totales(ColumnDescriptor col, Asociacion a, RecaudoListadoDto data)
    {
        TotalLinea(col, a, "TOTAL INGRESOS:", data.TotalIngresos, Colors.Green.Darken3);
        TotalLinea(col, a, "TOTAL EGRESOS:", data.TotalEgresos, Colors.Red.Darken3);
        TotalLinea(col, a, "BALANCE NETO:", data.Balance, Colors.Blue.Darken3);
    }

    private static void TotalLinea(ColumnDescriptor col, Asociacion a, string etiqueta, decimal valor, string color)
        => col.Item().AlignRight().Row(row =>
        {
            row.ConstantItem(120).Text(etiqueta).FontSize(10).Bold().FontColor(color);
            row.ConstantItem(90).AlignRight().Text(Money(a, valor)).FontSize(10).Bold().FontColor(color);
        });

    // ==================================================================
    //  MOROSIDAD
    // ==================================================================
    public byte[] Morosidad(Asociacion a, List<MorosidadDto> items) =>
        Generar(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Element(c => Encabezado(c, a, "Reporte de Morosidad"));
                page.Footer().Element(Pie);

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Background(Colors.Red.Lighten4).Padding(10).Column(c =>
                    {
                        c.Item().Text($"Socios con cuotas atrasadas: {items.Count}")
                            .FontSize(13).Bold().FontColor(Colors.Red.Darken3);
                        c.Item().Text($"Cuotas atrasadas: {items.Sum(i => i.CuotasAtrasadas)}").FontSize(10);
                        c.Item().Text($"Monto estimado pendiente: {Money(a, items.Sum(i => i.MontoEstimado))}")
                            .FontSize(10);
                    });

                    if (items.Count == 0)
                    {
                        col.Item().Text("No hay socios con cuotas atrasadas.")
                            .FontSize(12).FontColor(Colors.Green.Darken3);
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(70);
                                columns.RelativeColumn();
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(90);
                                columns.ConstantColumn(100);
                            });

                            table.Header(header =>
                            {
                                CeldaTitulo(header, "N° Socio");
                                CeldaTitulo(header, "Nombre");
                                CeldaTitulo(header, "Cuotas", true);
                                CeldaTitulo(header, "Desde");
                                CeldaTitulo(header, "Monto est.", true);
                            });

                            foreach (var i in items)
                            {
                                table.Cell().Padding(3).Text(i.Numero);
                                table.Cell().Padding(3).Text(i.Nombre);
                                table.Cell().Padding(3).AlignRight().Text(i.CuotasAtrasadas.ToString());
                                table.Cell().Padding(3).Text(i.PrimeraAtrasada?.ToString("dd/MM/yyyy") ?? "—");
                                table.Cell().Padding(3).AlignRight().Text(
                                    i.MontoEstimado > 0 ? Money(a, i.MontoEstimado) : "—");
                            }
                        });
                    }
                });
            });
        });

    // ==================================================================
    //  RESUMEN ANUAL
    // ==================================================================
    public byte[] ResumenAnual(Asociacion a, int anio, IReadOnlyList<ResumenMensualDto> meses) =>
        Generar(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Element(c => Encabezado(c, a, $"Resumen Anual {anio}"));
                page.Footer().Element(Pie);

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Background(Colors.Blue.Lighten4).Padding(10).Column(c =>
                    {
                        c.Item().Text($"Cuotas cobradas: {Money(a, meses.Sum(m => m.MontoCuotas))}")
                            .FontSize(13).Bold().FontColor(Colors.Blue.Darken3);
                        c.Item().Text($"Recaudos (ingresos): {Money(a, meses.Sum(m => m.IngresosExtra))}")
                            .FontSize(10);
                        c.Item().Text($"Recaudos (egresos): {Money(a, meses.Sum(m => m.EgresosExtra))}")
                            .FontSize(10);
                    });

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.ConstantColumn(60);
                            columns.ConstantColumn(100);
                            columns.ConstantColumn(100);
                            columns.ConstantColumn(100);
                            columns.ConstantColumn(100);
                        });

                        table.Header(header =>
                        {
                            CeldaTitulo(header, "Mes");
                            CeldaTitulo(header, "Cuotas", true);
                            CeldaTitulo(header, "Monto cuotas", true);
                            CeldaTitulo(header, "Ingresos ext.", true);
                            CeldaTitulo(header, "Egresos ext.", true);
                            CeldaTitulo(header, "Balance ext.", true);
                        });

                        foreach (var m in meses)
                        {
                            table.Cell().Padding(3).Text(PagoService.NombreMes(m.Mes));
                            table.Cell().Padding(3).AlignRight().Text(m.Cuotas.ToString());
                            table.Cell().Padding(3).AlignRight().Text(Money(a, m.MontoCuotas));
                            table.Cell().Padding(3).AlignRight().Text(Money(a, m.IngresosExtra));
                            table.Cell().Padding(3).AlignRight().Text(Money(a, m.EgresosExtra));
                            table.Cell().Padding(3).AlignRight().Text(Money(a, m.IngresosExtra - m.EgresosExtra));
                        }
                    });

                    col.Item().PaddingTop(6).AlignRight()
                        .Text($"TOTAL CUOTAS: {Money(a, meses.Sum(m => m.MontoCuotas))}")
                        .FontSize(12).Bold().FontColor(Colors.Blue.Darken3);
                });
            });
        });

    // ==================================================================
    //  BALANCE GENERAL
    // ==================================================================
    public byte[] BalanceGeneralDoc(Asociacion a, BalanceGeneralDto data) =>
        Generar(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Element(c => Encabezado(c, a, $"Balance General {data.Anio}"));
                page.Footer().Element(Pie);

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Row(row =>
                    {
                        Kpi(row, a, "Total general", data.TotalGeneral, Colors.Blue.Darken3, Colors.Blue.Lighten4);
                        row.ConstantItem(8);
                        Kpi(row, a, "Cuotas cobradas", data.TotalCuotas, Colors.Green.Darken3, Colors.Green.Lighten4);
                        row.ConstantItem(8);
                        Kpi(row, a, "Recaudos", data.TotalRecaudos, Colors.Orange.Darken3, Colors.Orange.Lighten4);
                        row.ConstantItem(8);
                        Kpi(row, a, "En mora", data.MontoMora, Colors.Red.Darken3, Colors.Red.Lighten4,
                            $"{data.SociosEnMora} socios");
                    });

                    col.Item().Text("Ingresos por socio").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(60);
                            columns.RelativeColumn();
                            columns.ConstantColumn(50);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(85);
                        });

                        table.Header(header =>
                        {
                            CeldaTitulo(header, "N° Socio");
                            CeldaTitulo(header, "Nombre");
                            CeldaTitulo(header, "Cuotas", true);
                            CeldaTitulo(header, "Total cuotas", true);
                            CeldaTitulo(header, "Recaudos", true);
                            CeldaTitulo(header, "Aportado", true);
                        });

                        foreach (var i in data.Ingresos)
                        {
                            table.Cell().Padding(3).Text(i.Numero);
                            table.Cell().Padding(3).Text(i.Nombre);
                            table.Cell().Padding(3).AlignRight().Text(i.CuotasPagadas.ToString());
                            table.Cell().Padding(3).AlignRight().Text(Money(a, i.TotalCuotas));
                            table.Cell().Padding(3).AlignRight().Text(Money(a, i.RecaudosAportados));
                            table.Cell().Padding(3).AlignRight().Text(Money(a, i.TotalAportado)).SemiBold();
                        }
                    });

                    col.Item().PaddingTop(6).Text("Socios en mora").FontSize(12).Bold();

                    if (data.Morosos.Count == 0)
                    {
                        col.Item().Text("No hay socios con cuotas atrasadas.")
                            .FontColor(Colors.Green.Darken3);
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(60);
                                columns.RelativeColumn();
                                columns.ConstantColumn(55);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(85);
                            });

                            table.Header(header =>
                            {
                                CeldaTitulo(header, "N° Socio");
                                CeldaTitulo(header, "Nombre");
                                CeldaTitulo(header, "Atrasadas", true);
                                CeldaTitulo(header, "Desde");
                                CeldaTitulo(header, "Último pago");
                                CeldaTitulo(header, "Monto est.", true);
                            });

                            foreach (var i in data.Morosos)
                            {
                                table.Cell().Padding(3).Text(i.Numero);
                                table.Cell().Padding(3).Text(i.Nombre);
                                table.Cell().Padding(3).AlignRight().Text(i.CuotasAtrasadas.ToString());
                                table.Cell().Padding(3).Text(i.PrimeraAtrasadaTexto);
                                table.Cell().Padding(3).Text(i.UltimoPagoTexto);
                                table.Cell().Padding(3).AlignRight().Text(
                                    i.MontoMora > 0 ? Money(a, i.MontoMora) : "—");
                            }
                        });
                    }
                });
            });
        });

    private static void Kpi(
        RowDescriptor row, Asociacion a, string titulo, decimal valor,
        string color, string fondo, string? extra = null)
    {
        row.RelativeItem().Background(fondo).Padding(8).Column(c =>
        {
            c.Item().Text(titulo).FontSize(9).FontColor(Colors.Grey.Darken2);
            c.Item().Text(Money(a, valor)).FontSize(13).Bold().FontColor(color);
            if (extra is not null) c.Item().Text(extra).FontSize(9);
        });
    }
}

public record ResumenMensualDto(int Mes, int Cuotas, decimal MontoCuotas, decimal IngresosExtra, decimal EgresosExtra);
