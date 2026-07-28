using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
            InvoiceSection targetSection)
        {
            return invoices
                .Where(invoice => IsSuspicious(invoice, targetSection))
                .ToList();
        }

        public static bool IsSuspicious(
            InvoiceData invoice,
            InvoiceSection targetSection)
        {
            return targetSection == InvoiceSection.Suppliers
                ? IsReferenceParty(invoice.SenderVatNumber, invoice.SenderName)
                : IsReferenceParty(
                    invoice.RecipientVatNumber,
                    string.IsNullOrWhiteSpace(invoice.RecipientName)
                        ? invoice.CompanyName
                        : invoice.RecipientName);
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
            string normalizedVat = NormalizeAlphaNumeric(vatNumber);
            string referenceVat = NormalizeAlphaNumeric(ReferenceVatNumber);
            if (!string.IsNullOrEmpty(normalizedVat))
            {
                return normalizedVat == referenceVat ||
                       normalizedVat == referenceVat.Substring(2);
            }

            string normalizedName = NormalizeAlphaNumeric(companyName);
            string referenceName = NormalizeAlphaNumeric(ReferenceCompanyName);
            if (normalizedName == referenceName)
                return true;

            if (normalizedName.StartsWith("OMT", StringComparison.Ordinal) &&
                normalizedName.Contains("TERZI", StringComparison.Ordinal) &&
                (normalizedName.Contains("GIANANTONIO", StringComparison.Ordinal) ||
                 normalizedName.EndsWith("G", StringComparison.Ordinal)))
                return true;

            return normalizedName.Length >= 18 &&
                   EditDistance(normalizedName, referenceName) <= 2;
        }

        private static string NormalizeAlphaNumeric(string? value)
        {
            string decomposed = (value ?? string.Empty)
                .Normalize(NormalizationForm.FormD);
            return new string(decomposed
                .Where(character =>
                    CharUnicodeInfo.GetUnicodeCategory(character) !=
                    UnicodeCategory.NonSpacingMark)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        private static int EditDistance(string first, string second)
        {
            var previous = Enumerable.Range(0, second.Length + 1).ToArray();
            var current = new int[second.Length + 1];
            for (int row = 1; row <= first.Length; row++)
            {
                current[0] = row;
                for (int column = 1; column <= second.Length; column++)
                {
                    int substitution = previous[column - 1] +
                        (first[row - 1] == second[column - 1] ? 0 : 1);
                    current[column] = Math.Min(
                        Math.Min(previous[column] + 1, current[column - 1] + 1),
                        substitution);
                }
                (previous, current) = (current, previous);
            }
            return previous[second.Length];
        }
    }
}
