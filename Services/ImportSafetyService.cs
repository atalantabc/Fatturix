using System;
using System.Collections.Generic;
using System.Linq;
using FattureViewer.Models;

namespace FattureViewer.Services
{
    public static class ImportSafetyService
    {
        public const string ReferenceCompanyName = "O.M.T. DI TERZI GIANANTONIO";
        public const string ReferenceVatNumber = "IT04170380374";
        public const int WarningThreshold = 5;

        public static bool RequiresWarning(
            IEnumerable<InvoiceData> invoices,
            InvoiceSection targetSection)
        {
            int matches = invoices.Count(invoice =>
                targetSection == InvoiceSection.Suppliers
                    ? IsReferenceParty(invoice.SenderVatNumber, invoice.SenderName)
                    : IsReferenceParty(
                        invoice.RecipientVatNumber,
                        string.IsNullOrWhiteSpace(invoice.RecipientName)
                            ? invoice.CompanyName
                            : invoice.RecipientName));
            return matches > WarningThreshold;
        }

        public static bool CanContinueAfterWarning(bool warningRequired, bool importAnyway)
        {
            return !warningRequired || importAnyway;
        }

        public static bool IsReferenceParty(string? vatNumber, string? companyName)
        {
            string normalizedVat = NormalizeAlphaNumeric(vatNumber);
            string referenceVat = NormalizeAlphaNumeric(ReferenceVatNumber);
            if (!string.IsNullOrEmpty(normalizedVat) &&
                (normalizedVat == referenceVat ||
                 normalizedVat == referenceVat.Substring(2)))
                return true;

            return string.IsNullOrWhiteSpace(normalizedVat) &&
                   NormalizeAlphaNumeric(companyName) ==
                   NormalizeAlphaNumeric(ReferenceCompanyName);
        }

        private static string NormalizeAlphaNumeric(string? value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }
    }
}
