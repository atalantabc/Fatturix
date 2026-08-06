using System;
using System.Collections.Generic;
using System.Linq;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public enum ImportReviewChoice
    {
        Cancel,
        All,
        Selected
    }

    public sealed class ImportReviewResult
    {
        public ImportReviewChoice Choice { get; init; }
        public IReadOnlyCollection<string> SelectedInvoiceIds { get; init; } =
            Array.Empty<string>();
    }

    public static class ImportSafetyService
    {
        public const string ReferenceCompanyName = "O.M.T. DI TERZI GIANANTONIO";
        public const string ReferenceVatNumber = "IT02745400164";

        public static List<InvoiceData> GetSuspiciousInvoices(
            IEnumerable<InvoiceData> invoices,
            InvoiceSection targetSection,
            string? referenceVatNumber = null)
        {
            return invoices
                .Where(invoice => IsSuspicious(
                    invoice,
                    targetSection,
                    referenceVatNumber))
                .ToList();
        }

        public static bool IsSuspicious(
            InvoiceData invoice,
            InvoiceSection targetSection,
            string? referenceVatNumber = null)
        {
            string companyVat = string.IsNullOrWhiteSpace(referenceVatNumber)
                ? AppProfileService.Current.CompanyVatNumber
                : referenceVatNumber;
            return targetSection == InvoiceSection.Suppliers
                ? IsReferenceVat(invoice.SenderVatNumber, companyVat)
                : IsReferenceVat(invoice.RecipientVatNumber, companyVat);
        }

        public static List<InvoiceData>? ResolveImport(
            IEnumerable<InvoiceData> invoices,
            ImportReviewResult result)
        {
            List<InvoiceData> all = invoices.ToList();
            if (result.Choice == ImportReviewChoice.Cancel)
                return null;
            if (result.Choice == ImportReviewChoice.All)
                return all;

            var selected = new HashSet<string>(
                result.SelectedInvoiceIds,
                StringComparer.Ordinal);
            return all.Where(invoice => selected.Contains(invoice.Id)).ToList();
        }

        public static string GetWarningMessage(
            int count,
            InvoiceSection targetSection)
        {
            string kind = targetSection == InvoiceSection.Customers
                ? (count == 1 ? "Fornitore" : "Fornitori")
                : (count == 1 ? "Cliente" : "Clienti");
            return count == 1
                ? $"È stata trovata 1 fattura che sembra essere una fattura {kind}."
                : $"Sono state trovate {count} fatture che sembrano essere fatture {kind}.";
        }

        public static bool IsReferenceParty(string? vatNumber, string? companyName)
        {
            return IsReferenceVat(
                vatNumber,
                AppProfileService.Current.CompanyVatNumber);
        }

        public static bool IsReferenceVat(
            string? vatNumber,
            string referenceVatNumber)
        {
            string normalizedVat = NormalizeAlphaNumeric(vatNumber);
            string referenceVat = NormalizeAlphaNumeric(referenceVatNumber);
            if (string.IsNullOrEmpty(normalizedVat) ||
                string.IsNullOrEmpty(referenceVat))
                return false;

            return RemoveItalianPrefix(normalizedVat) ==
                   RemoveItalianPrefix(referenceVat);
        }

        private static string NormalizeAlphaNumeric(string? value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        private static string RemoveItalianPrefix(string vatNumber)
        {
            return vatNumber.StartsWith("IT", StringComparison.Ordinal)
                ? vatNumber.Substring(2)
                : vatNumber;
        }
    }
}
