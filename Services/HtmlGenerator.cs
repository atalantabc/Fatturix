using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public class HtmlGenerator
    {
        public static string GenerateInvoiceHtml(InvoiceData data, string xmlContent)
        {
            if (string.IsNullOrEmpty(xmlContent))
                return "<html><body><p>Nessun contenuto disponibile per questa fattura.</p></body></html>";

            XDocument doc;
            try
            {
                doc = XDocument.Parse(xmlContent);
            }
            catch
            {
                return "<html><body><p>Impossibile analizzare l'XML per l'anteprima formattata.</p></body></html>";
            }

            XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
            var header = doc.Descendants(ns + "FatturaElettronicaHeader").FirstOrDefault();
            var body = doc.Descendants(ns + "FatturaElettronicaBody").FirstOrDefault();

            // Extract detailed fields
            string cedenteIndirizzo = header?.Descendants(ns + "CedentePrestatore").Descendants(ns + "Sede").Elements(ns + "Indirizzo").FirstOrDefault()?.Value ?? "";
            string cedenteComune = header?.Descendants(ns + "CedentePrestatore").Descendants(ns + "Sede").Elements(ns + "Comune").FirstOrDefault()?.Value ?? "";
            string cedenteCap = header?.Descendants(ns + "CedentePrestatore").Descendants(ns + "Sede").Elements(ns + "CAP").FirstOrDefault()?.Value ?? "";
            string cedentePIva = header?.Descendants(ns + "CedentePrestatore").Descendants(ns + "IdFiscaleIVA").Elements(ns + "IdCodice").FirstOrDefault()?.Value ?? "";
            string cedenteCF = header?.Descendants(ns + "CedentePrestatore").Descendants(ns + "DatiAnagrafici").Elements(ns + "CodiceFiscale").FirstOrDefault()?.Value ?? cedentePIva;

            string cessionarioIndirizzo = header?.Descendants(ns + "CessionarioCommittente").Descendants(ns + "Sede").Elements(ns + "Indirizzo").FirstOrDefault()?.Value ?? "";
            string cessionarioComune = header?.Descendants(ns + "CessionarioCommittente").Descendants(ns + "Sede").Elements(ns + "Comune").FirstOrDefault()?.Value ?? "";
            string cessionarioCap = header?.Descendants(ns + "CessionarioCommittente").Descendants(ns + "Sede").Elements(ns + "CAP").FirstOrDefault()?.Value ?? "";
            string cessionarioPIva = header?.Descendants(ns + "CessionarioCommittente").Descendants(ns + "IdFiscaleIVA").Elements(ns + "IdCodice").FirstOrDefault()?.Value ?? "";
            string cessionarioCF = header?.Descendants(ns + "CessionarioCommittente").Descendants(ns + "DatiAnagrafici").Elements(ns + "CodiceFiscale").FirstOrDefault()?.Value ?? cessionarioPIva;

            var datiGenerali = body?.Element(ns + "DatiGenerali")?.Element(ns + "DatiGeneraliDocumento");
            string tipoDoc = datiGenerali?.Element(ns + "TipoDocumento")?.Value ?? "TD01";
            string numeroFattura = datiGenerali?.Element(ns + "Numero")?.Value ?? "";
            string causale = string.Join("<br/>", datiGenerali?.Elements(ns + "Causale").Select(x => x.Value) ?? new List<string>());

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'><style>");
            sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 12px; margin: 0; padding: 20px; color: #000; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-bottom: 10px; }");
            sb.AppendLine("th, td { border: 1px solid #000; padding: 5px; text-align: left; vertical-align: top; }");
            sb.AppendLine("th { background-color: #f0f0f0; font-weight: bold; font-size: 11px; }");
            sb.AppendLine(".title { font-weight: bold; font-size: 16px; margin-bottom: 5px; text-transform: uppercase; }");
            sb.AppendLine(".box { border: 1px solid #000; padding: 10px; margin-bottom: 10px; height: 100%; box-sizing: border-box; }");
            sb.AppendLine(".grid-2 { display: table; width: 100%; margin-bottom: 10px; border-spacing: 0; }");
            sb.AppendLine(".col-2 { display: table-cell; width: 50%; }");
            sb.AppendLine(".col-left { padding-right: 5px; }");
            sb.AppendLine(".col-right { padding-left: 5px; }");
            sb.AppendLine(".num-right { text-align: right; }");
            sb.AppendLine("</style></head><body>");

            // Header Boxes (Mittente / Destinatario)
            sb.AppendLine("<div class='grid-2'>");
            
            sb.AppendLine("<div class='col-2 col-left'><div class='box'>");
            sb.AppendLine("<div class='title'>MITTENTE</div>");
            sb.AppendLine($"<div>Identificativo fiscale ai fini IVA: IT{cedentePIva}</div>");
            sb.AppendLine($"<div>Codice fiscale: {cedenteCF}</div>");
            sb.AppendLine($"<div>Denominazione: <strong>{System.Net.WebUtility.HtmlEncode(data.SenderName)}</strong></div>");
            sb.AppendLine($"<div>Indirizzo: {System.Net.WebUtility.HtmlEncode(cedenteIndirizzo)}</div>");
            sb.AppendLine($"<div>Comune: {System.Net.WebUtility.HtmlEncode(cedenteComune)} Cap: {cedenteCap}</div>");
            sb.AppendLine("</div></div>");

            sb.AppendLine("<div class='col-2 col-right'><div class='box'>");
            sb.AppendLine("<div class='title'>DESTINATARIO</div>");
            sb.AppendLine($"<div>Identificativo fiscale ai fini IVA: IT{cessionarioPIva}</div>");
            sb.AppendLine($"<div>Codice fiscale: {cessionarioCF}</div>");
            sb.AppendLine($"<div>Denominazione: <strong>{System.Net.WebUtility.HtmlEncode(data.CompanyName)}</strong></div>");
            sb.AppendLine($"<div>Indirizzo: {System.Net.WebUtility.HtmlEncode(cessionarioIndirizzo)}</div>");
            sb.AppendLine($"<div>Comune: {System.Net.WebUtility.HtmlEncode(cessionarioComune)} Cap: {cessionarioCap}</div>");
            sb.AppendLine("</div></div>");
            
            sb.AppendLine("</div>");

            // General Data Table
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>TIPOLOGIA DOCUMENTO</th><th>CAUSALE</th><th>NUMERO FATTURA</th><th>DATA</th></tr>");
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{tipoDoc}</td>");
            sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(causale)}</td>");
            sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(numeroFattura)}</td>");
            sb.AppendLine($"<td>{data.Date:dd-MM-yyyy}</td>");
            sb.AppendLine("</tr>");
            sb.AppendLine("</table>");

            // Items Table
            var linee = body?.Descendants(ns + "DettaglioLinee");
            if (linee != null && linee.Any())
            {
                sb.AppendLine("<table>");
                sb.AppendLine("<tr>");
                sb.AppendLine("<th>COD.ARTICOLO</th><th>DESCRIZIONE</th><th class='num-right'>QUANTITA</th><th class='num-right'>PREZZO UNITARIO</th><th class='num-right'>%IVA</th><th class='num-right'>PREZZO TOTALE</th>");
                sb.AppendLine("</tr>");

                foreach (var linea in linee)
                {
                    string codArt = linea.Descendants(ns + "CodiceArticolo").Elements(ns + "CodiceValore").FirstOrDefault()?.Value ?? "";
                    string desc = linea.Element(ns + "Descrizione")?.Value ?? "";
                    
                    decimal qta = 0;
                    decimal.TryParse(linea.Element(ns + "Quantita")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out qta);
                    
                    decimal pu = 0;
                    decimal.TryParse(linea.Element(ns + "PrezzoUnitario")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out pu);
                    
                    decimal iva = 0;
                    decimal.TryParse(linea.Element(ns + "AliquotaIVA")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out iva);
                    
                    decimal pt = 0;
                    decimal.TryParse(linea.Element(ns + "PrezzoTotale")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out pt);

                    sb.AppendLine("<tr>");
                    sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(codArt)}</td>");
                    sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(desc)}</td>");
                    sb.AppendLine($"<td class='num-right'>{qta.ToString("N2", new CultureInfo("it-IT"))}</td>");
                    sb.AppendLine($"<td class='num-right'>{pu.ToString("N4", new CultureInfo("it-IT"))}</td>");
                    sb.AppendLine($"<td class='num-right'>{iva.ToString("N2", new CultureInfo("it-IT"))}</td>");
                    sb.AppendLine($"<td class='num-right'>{pt.ToString("N2", new CultureInfo("it-IT"))}</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }

            // 1. RIEPILOGHI IVA E TOTALI
            var riepiloghi = body?.Descendants(ns + "DatiBeniServizi").Descendants(ns + "DatiRiepilogo");
            if (riepiloghi != null && riepiloghi.Any())
            {
                sb.AppendLine("<div style='background-color:#999; color:#fff; text-align:center; font-weight:bold; padding:3px; font-size:11px; margin-top:10px;'>RIEPILOGHI IVA E TOTALI</div>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>esigibilità iva / riferimenti normativi</th><th class='num-right'>%IVA</th><th class='num-right'>Spese accessorie</th><th class='num-right'>Arr.</th><th class='num-right'>Totale imponibile</th><th class='num-right'>Totale imposta</th></tr>");

                foreach (var r in riepiloghi)
                {
                    string esig = r.Element(ns + "EsigibilitaIVA")?.Value ?? "";
                    string rifNorm = r.Element(ns + "RiferimentoNormativo")?.Value ?? "";
                    string esigStr = esig == "D" ? "D (esigibilità differita)" : (esig == "I" ? "I (esigibilità immediata)" : (esig == "S" ? "S (scissione dei pagamenti)" : esig));
                    string desc = !string.IsNullOrEmpty(rifNorm) ? rifNorm : esigStr;

                    decimal iva = 0; decimal.TryParse(r.Element(ns + "AliquotaIVA")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out iva);
                    decimal spese = 0; decimal.TryParse(r.Element(ns + "SpeseAccessorie")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out spese);
                    decimal arr = 0; decimal.TryParse(r.Element(ns + "Arrotondamento")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out arr);
                    decimal imponibile = 0; decimal.TryParse(r.Element(ns + "ImponibileImporto")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out imponibile);
                    decimal imposta = 0; decimal.TryParse(r.Element(ns + "Imposta")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out imposta);

                    sb.AppendLine("<tr>");
                    sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(desc)}</td>");
                    sb.AppendLine($"<td class='num-right'>{iva.ToString("N2", new CultureInfo("it-IT"))}</td>");
                    sb.AppendLine($"<td class='num-right'>{(spese != 0 ? spese.ToString("N2", new CultureInfo("it-IT")) : "")}</td>");
                    sb.AppendLine($"<td class='num-right'>{(arr != 0 ? arr.ToString("N2", new CultureInfo("it-IT")) : "")}</td>");
                    sb.AppendLine($"<td class='num-right'>{imponibile.ToString("N2", new CultureInfo("it-IT"))}</td>");
                    sb.AppendLine($"<td class='num-right'>{imposta.ToString("N2", new CultureInfo("it-IT"))}</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }

            // 2. Bollo / Valuta / Totale Documento
            string valuta = datiGenerali?.Element(ns + "Divisa")?.Value ?? "EUR";
            string importoBollo = datiGenerali?.Element(ns + "DatiBollo")?.Element(ns + "ImportoBollo")?.Value ?? "";
            
            var sconti = datiGenerali?.Elements(ns + "ScontoMaggiorazione");
            string scontoMag = "";
            if (sconti != null && sconti.Any()) {
                decimal ts = 0;
                foreach (var s in sconti) {
                    decimal v = 0; decimal.TryParse(s.Element(ns + "Importo")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out v);
                    ts += s.Element(ns + "Tipo")?.Value == "SC" ? -v : v;
                }
                if (ts != 0) scontoMag = ts.ToString("N2", new CultureInfo("it-IT"));
            }

            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th style='width:20%;'>Importo bollo</th><th style='width:20%;'>Sconto/Maggiorazione</th><th style='width:10%;'>Arr.</th><th style='width:20%;'>Valuta</th><th class='num-right' style='width:30%;'>Totale documento</th></tr>");
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{importoBollo}</td>");
            sb.AppendLine($"<td>{scontoMag}</td>");
            sb.AppendLine($"<td></td>");
            sb.AppendLine($"<td style='text-align:center;'>{valuta}</td>");
            sb.AppendLine($"<td class='num-right' style='font-weight:bold;'>{data.TotalAmount.ToString("N2", new CultureInfo("it-IT"))}</td>");
            sb.AppendLine("</tr>");
            sb.AppendLine("</table>");

            // 3. DATI PAGAMENTO
            var pagamenti = body?.Descendants(ns + "DatiPagamento").Descendants(ns + "DettaglioPagamento");
            if (pagamenti != null && pagamenti.Any())
            {
                sb.AppendLine("<table style='margin-top:15px;'>");
                sb.AppendLine("<tr><th>Modalità pagamento</th><th>IBAN</th><th>Istituto</th><th>Data scadenza</th><th class='num-right'>Importo</th></tr>");
                
                foreach (var p in pagamenti)
                {
                    string mod = p.Element(ns + "ModalitaPagamento")?.Value ?? "";
                    string iban = p.Element(ns + "IBAN")?.Value ?? "";
                    string istituto = p.Element(ns + "IstitutoFinanziario")?.Value ?? "";
                    
                    string dataScadenza = "";
                    if (DateTime.TryParseExact(p.Element(ns + "DataScadenzaPagamento")?.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime scad))
                        dataScadenza = scad.ToString("dd-MM-yyyy");

                    decimal importoPag = 0; decimal.TryParse(p.Element(ns + "ImportoPagamento")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out importoPag);
                    
                    sb.AppendLine("<tr>");
                    sb.AppendLine($"<td>{mod}</td>");
                    sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(iban)}</td>");
                    sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(istituto)}</td>");
                    sb.AppendLine($"<td>{dataScadenza}</td>");
                    sb.AppendLine($"<td class='num-right'>{importoPag.ToString("N2", new CultureInfo("it-IT"))}</td>");
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</table>");
            }

            sb.AppendLine("</body></html>");

            return sb.ToString();
        }
    }
}
