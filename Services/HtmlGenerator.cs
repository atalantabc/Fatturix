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

            if (doc.Root != null)
            {
                foreach (var element in doc.Root.DescendantsAndSelf().ToList())
                    element.Name = element.Name.LocalName;
            }
            XNamespace ns = XNamespace.None;
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
            string codiceDestinatario = header?.Descendants(ns + "CodiceDestinatario").FirstOrDefault()?.Value ?? data.RecipientCode;

            var datiGenerali = body?.Descendants(ns + "DatiGeneraliDocumento").FirstOrDefault();
            string tipoDoc = datiGenerali?.Element(ns + "TipoDocumento")?.Value ?? "TD01";
            string numeroFattura = datiGenerali?.Element(ns + "Numero")?.Value ?? "";
            string causale = string.Join("<br/>", datiGenerali?.Elements(ns + "Causale").Select(x => System.Net.WebUtility.HtmlEncode(x.Value)) ?? new List<string>());

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta http-equiv='Content-Type' content='text/html;charset=UTF-8'>");
            sb.AppendLine("<style>");
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
            
            // Custom context menu styles
            sb.AppendLine("#customContextMenu {");
            sb.AppendLine("    display: none; position: absolute; background-color: white;");
            sb.AppendLine("    border: 1px solid #ccc; box-shadow: 2px 2px 5px rgba(0,0,0,0.2);");
            sb.AppendLine("    z-index: 1000; min-width: 150px; font-family: 'Segoe UI'; font-size: 13px;");
            sb.AppendLine("}");
            sb.AppendLine("#customContextMenu div { padding: 8px 15px; cursor: pointer; color: #333; }");
            sb.AppendLine("#customContextMenu div:hover { background-color: #0078D7; color: white; }");
            
            sb.AppendLine("</style>");
            sb.AppendLine("<script>");
            sb.AppendLine("window.onerror = function() { return true; };");
            sb.AppendLine("function printPreview() {");
            sb.AppendLine("    try {");
            sb.AppendLine("        var OLECMDID_PRINTPREVIEW = 7; var PROMPT = 1;");
            sb.AppendLine("        var wb = document.getElementById('ieWebBrowserControl');");
            sb.AppendLine("        if(!wb) { document.body.insertAdjacentHTML('beforeend', '<OBJECT ID=\"ieWebBrowserControl\" WIDTH=0 HEIGHT=0 CLASSID=\"CLSID:8856F961-340A-11D0-A96B-00C04FD705A2\"></OBJECT>'); wb = document.getElementById('ieWebBrowserControl'); }");
            sb.AppendLine("        wb.ExecWB(OLECMDID_PRINTPREVIEW, PROMPT);");
            sb.AppendLine("    } catch(e) { window.print(); }");
            sb.AppendLine("    hideMenu();");
            sb.AppendLine("}");
            sb.AppendLine("function hideMenu() { var m = document.getElementById('customContextMenu'); if(m) m.style.display = 'none'; }");
            sb.AppendLine("document.oncontextmenu = function(e) {");
            sb.AppendLine("    var evt = e || window.event;");
            sb.AppendLine("    if(evt.preventDefault) { evt.preventDefault(); } else { evt.returnValue = false; }");
            sb.AppendLine("    var m = document.getElementById('customContextMenu');");
            sb.AppendLine("    var px = evt.pageX || (evt.clientX + (document.documentElement.scrollLeft || document.body.scrollLeft));");
            sb.AppendLine("    var py = evt.pageY || (evt.clientY + (document.documentElement.scrollTop || document.body.scrollTop));");
            sb.AppendLine("    m.style.left = px + 'px'; m.style.top = py + 'px'; m.style.display = 'block';");
            sb.AppendLine("};");
            sb.AppendLine("document.onclick = hideMenu;");
            sb.AppendLine("</script>");
            sb.AppendLine("</head><body>");
            
            // Custom context menu HTML
            sb.AppendLine("<div id='customContextMenu'>");
            sb.AppendLine("    <div onclick='document.execCommand(\"copy\"); hideMenu();'>Copia</div>");
            sb.AppendLine("    <div onclick='document.execCommand(\"selectAll\"); hideMenu();'>Seleziona Tutto</div>");
            sb.AppendLine("    <div style='border-top: 1px solid #eee;' onclick='window.print(); hideMenu();'>Stampa...</div>");
            sb.AppendLine("    <div onclick='printPreview();'>Anteprima di stampa</div>");
            sb.AppendLine("</div>");

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
            sb.AppendLine("<tr><th>TIPOLOGIA DOCUMENTO</th><th>CAUSALE</th><th>NUMERO FATTURA</th><th>DATA</th><th>CODICE DESTINATARIO</th></tr>");
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{tipoDoc}</td>");
            sb.AppendLine($"<td>{causale}</td>");
            sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(numeroFattura)}</td>");
            sb.AppendLine($"<td>{data.Date:dd-MM-yyyy}</td>");
            sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(codiceDestinatario)}</td>");
            sb.AppendLine("</tr>");
            sb.AppendLine("</table>");

            var referenceTypes = new[]
            {
                (Tag: "DatiOrdineAcquisto", Label: "Ordine acquisto", NumberTag: "IdDocumento", DateTag: "Data"),
                (Tag: "DatiContratto", Label: "Contratto", NumberTag: "IdDocumento", DateTag: "Data"),
                (Tag: "DatiConvenzione", Label: "Convenzione", NumberTag: "IdDocumento", DateTag: "Data"),
                (Tag: "DatiRicezione", Label: "Ricezione", NumberTag: "IdDocumento", DateTag: "Data"),
                (Tag: "DatiFattureCollegate", Label: "Fattura collegata", NumberTag: "IdDocumento", DateTag: "Data"),
                (Tag: "DatiSAL", Label: "SAL", NumberTag: "RiferimentoFase", DateTag: "Data"),
                (Tag: "DatiDDT", Label: "DDT", NumberTag: "NumeroDDT", DateTag: "DataDDT")
            };
            var unboundReferences = body == null ? new List<string>() : referenceTypes.SelectMany(type => body.Descendants(ns + type.Tag)
                .Where(reference => !reference.Elements(ns + "RiferimentoNumeroLinea").Any())
                .Select(reference => FormatReferenceDescription(type.Label,
                    reference.Element(ns + type.NumberTag)?.Value ?? "",
                    reference.Element(ns + type.DateTag)?.Value ?? "")))
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .ToList();

            // Items Table
            var linee = body?.Descendants(ns + "DettaglioLinee").ToList() ?? new List<XElement>();
            if (linee.Count > 0 || unboundReferences.Count > 0)
            {
                sb.AppendLine("<table>");
                sb.AppendLine("<tr>");
                sb.AppendLine("<th>COD. ARTICOLO</th><th>DESCRIZIONE</th><th class='num-right'>QUANTITA</th><th class='num-right'>PREZZO UNITARIO</th><th>UM</th><th>SCONTO / OMAGGIO</th><th class='num-right'>%IVA</th><th class='num-right'>PREZZO TOTALE</th>");
                sb.AppendLine("</tr>");

                foreach (string reference in unboundReferences)
                    AppendDescriptionRow(sb, reference);


                foreach (var linea in linee)
                {
                    string codArt = linea.Descendants(ns + "CodiceArticolo").Elements(ns + "CodiceValore").FirstOrDefault()?.Value ?? "";
                    string desc = linea.Element(ns + "Descrizione")?.Value ?? "";
                    string unit = linea.Element(ns + "UnitaMisura")?.Value ?? "";
                    string discount = FormatLineDiscount(linea, ns, desc);

                    bool hasQuantity = decimal.TryParse(linea.Element(ns + "Quantita")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal quantity);
                    bool hasUnitPrice = decimal.TryParse(linea.Element(ns + "PrezzoUnitario")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal unitPrice);
                    bool hasVat = decimal.TryParse(linea.Element(ns + "AliquotaIVA")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal vat);
                    bool hasTotal = decimal.TryParse(linea.Element(ns + "PrezzoTotale")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal total);
                    bool isDescriptionOnly = string.IsNullOrWhiteSpace(codArt) && !hasQuantity && unitPrice == 0 && total == 0 && string.IsNullOrWhiteSpace(unit) && string.IsNullOrWhiteSpace(discount);
                    if (isDescriptionOnly)
                    {
                        AppendDescriptionRow(sb, desc);
                        continue;
                    }

                    sb.AppendLine("<tr>");
                    sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(codArt)}</td>");
                    sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(desc)}</td>");
                    sb.AppendLine($"<td class='num-right'>{(hasQuantity ? quantity.ToString("N2", new CultureInfo("it-IT")) : "")}</td>");
                    sb.AppendLine($"<td class='num-right'>{(hasUnitPrice ? unitPrice.ToString("N4", new CultureInfo("it-IT")) : "")}</td>");
                    sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(unit)}</td>");
                    sb.AppendLine($"<td>{discount}</td>");
                    sb.AppendLine($"<td class='num-right'>{(hasVat ? vat.ToString("N2", new CultureInfo("it-IT")) : "")}</td>");
                    sb.AppendLine($"<td class='num-right'>{(hasTotal ? total.ToString("N2", new CultureInfo("it-IT")) : "")}</td>");
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

        private static void AppendDescriptionRow(StringBuilder sb, string description)
        {
            sb.AppendLine("<tr class='reference-row'>");
            sb.AppendLine($"<td></td><td>{System.Net.WebUtility.HtmlEncode(description)}</td>");
            sb.AppendLine("<td class='num-right'></td><td class='num-right'></td><td></td><td></td><td class='num-right'></td><td class='num-right'></td>");
            sb.AppendLine("</tr>");
        }

        private static string FormatReferenceDescription(string label, string number, string date)
        {
            if (DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedDate))
                date = parsedDate.ToString("dd-MM-yyyy");

            string prefix = label == "Ordine acquisto"
                ? number.Equals("MAIL", StringComparison.OrdinalIgnoreCase)
                    ? "VS ordine mail"
                    : $"VS ordine{(string.IsNullOrWhiteSpace(number) ? "" : " " + number)}"
                : $"{label}{(string.IsNullOrWhiteSpace(number) ? "" : " " + number)}";
            return string.IsNullOrWhiteSpace(date) ? prefix : $"{prefix} del {date}";
        }

        private static string FormatLineDiscount(XElement line, XNamespace ns, string description)
        {
            var values = new List<string>();
            var culture = new CultureInfo("it-IT");
            foreach (var discount in line.Elements(ns + "ScontoMaggiorazione"))
            {
                string sign = discount.Element(ns + "Tipo")?.Value == "MG" ? "+" : "-";
                if (decimal.TryParse(discount.Element(ns + "Percentuale")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal percentage))
                    values.Add(sign + Math.Abs(percentage).ToString("N2", culture) + "%");
                else if (decimal.TryParse(discount.Element(ns + "Importo")?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount))
                    values.Add(sign + Math.Abs(amount).ToString("N2", culture));
            }

            bool isGift = description.IndexOf("omaggio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          line.Descendants(ns + "AltriDatiGestionali").Any(value => value.Value.IndexOf("omaggio", StringComparison.OrdinalIgnoreCase) >= 0);
            if (isGift)
                values.Add("OMAGGIO");

            if (values.Count == 0)
            {
                string transferType = line.Element(ns + "TipoCessionePrestazione")?.Value ?? "";
                string label = transferType switch { "SC" => "SCONTO", "PR" => "PREMIO", "AB" => "ABBUONO", "AC" => "SPESA ACCESSORIA", _ => "" };
                if (!string.IsNullOrEmpty(label))
                    values.Add(label);
            }

            return string.Join("<br/>", values.Distinct(StringComparer.OrdinalIgnoreCase).Select(System.Net.WebUtility.HtmlEncode));
        }
    }
}
