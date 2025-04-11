using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BondYieldEstimator
{
    public enum PaymentFrequency
    {
        Annual = 1,
        SemiAnnual = 2
    }

    public struct Bond
    {
        public double Notional;
        public double CouponRate;
        public double Yield;
        public PaymentFrequency Frequency;
        public DateTime EvaluationDate;
        public DateTime MaturityDate;
        public double Price;
        public double YieldFirstOrder;
        public double YieldEstimate;
        public double YieldSecondOrder;
        public double ErrorFirstOrder;
        public double ErrorEstimate;
        public double ErrorSecondOrder;

        public Bond(double notional, double couponRate, double yield, PaymentFrequency freq,
                    DateTime evaluationDate, DateTime maturityDate)
        {
            Notional = notional;
            CouponRate = couponRate;
            Yield = yield;
            Frequency = freq;
            EvaluationDate = evaluationDate;
            MaturityDate = maturityDate.ToBusinessDay();

            var cashFlows = BondUtils.GenerateCashFlows(notional, couponRate, freq, evaluationDate, MaturityDate);
            var paymentDates = BondUtils.GeneratePaymentDates(freq, evaluationDate, MaturityDate);
            Price = BondUtils.PresentValue(cashFlows, evaluationDate, paymentDates, yield);

            double principal = notional;
            YieldFirstOrder = BondUtils.InitialYieldEstimateFirstOrder(notional, principal, couponRate, evaluationDate, MaturityDate, Price);
            YieldEstimate = BondUtils.InitialYieldEstimate(notional, principal, couponRate, evaluationDate, MaturityDate, Price);
            YieldSecondOrder = BondUtils.InitialYieldEstimateSecondOrder(notional, principal, couponRate, evaluationDate, MaturityDate, Price);

            ErrorFirstOrder = YieldFirstOrder - Yield;
            ErrorEstimate = YieldEstimate - Yield;
            ErrorSecondOrder = YieldSecondOrder - Yield;
        }
    }

    public static class BondUtils
    {
        public static double CalendarDaysPerYear = 365.25;

        public static IEnumerable<Bond> GenerateUniqueBonds(DateTime evaluationDate, double notional, double[] couponRates, double[] yields, PaymentFrequency[] frequencies, int maxYears)
        {
            HashSet<(double, double, PaymentFrequency, DateTime)> seen = new();
            List<Bond> uniqueBonds = new();

            for (int d = 1; d <= maxYears * 365; d++)
            {
                DateTime rawDate = evaluationDate.AddDays(d);
                DateTime maturity = rawDate.ToBusinessDay();

                foreach (var cr in couponRates)
                foreach (var yld in yields)
                foreach (var freq in frequencies)
                {
                    var key = (cr, yld, freq, maturity);
                    if (seen.Add(key))
                    {
                        Bond bond = new Bond(notional, cr, yld, freq, evaluationDate, maturity);
                        uniqueBonds.Add(bond);
                    }
                }
            }

            return uniqueBonds;
        }

        public static DateTime[] GeneratePaymentDates(PaymentFrequency frequency, DateTime evaluationDate, DateTime maturityDate)
        {
            int monthsBetweenPayments = 12 / (int)frequency;
            List<DateTime> paymentDates = new();
            DateTime date = maturityDate;

            while (date > evaluationDate)
            {
                paymentDates.Add(date.ToBusinessDay());
                date = date.AddMonths(-monthsBetweenPayments);
            }

            paymentDates.Sort();
            return paymentDates.ToArray();
        }

        public static double[] GenerateCashFlows(double notional, double couponRate, PaymentFrequency frequency, DateTime evaluationDate, DateTime maturityDate)
        {
            var paymentDates = GeneratePaymentDates(frequency, evaluationDate, maturityDate);
            int n = paymentDates.Length;
            double[] cashFlows = new double[n];

            for (int i = 0; i < n; i++)
                cashFlows[i] = notional * couponRate / (int)frequency;

            cashFlows[n - 1] += notional;
            return cashFlows;
        }

        public static double PresentValue(double[] cashFlows, DateTime evaluationDate, DateTime[] paymentDates, double yield)
        {
            double pv = 0;
            for (int i = 0; i < paymentDates.Length; i++)
            {
                double years = (paymentDates[i] - evaluationDate).TotalDays / CalendarDaysPerYear;
                double df = Math.Pow(1.0 + yield, -years);
                pv += df * cashFlows[i];
            }
            return pv;
        }

        public static double InitialYieldEstimateFirstOrder(double notional, double principal, double couponRate, DateTime evaluationDate, DateTime maturityDate, double price)
        {
            double T = (maturityDate - evaluationDate).Days / CalendarDaysPerYear;
            double c = couponRate * notional;
            double initialYieldEstimate = (principal + T * c - price) / (0.5 * T * (T + 1) * c + T * principal);
            return Math.Max(0, initialYieldEstimate);
        }

        public static double InitialYieldEstimate(double notional, double principal, double couponRate, DateTime evaluationDate, DateTime maturityDate, double price)
        {
            double T = (maturityDate - evaluationDate).Days / CalendarDaysPerYear;
            double c = couponRate * notional;
            double initialYieldEstimate = (principal + T * c - price) / (price + (T - 1) * (principal + 0.5 * T * c));
            return Math.Max(0, initialYieldEstimate);
        }

        public static double InitialYieldEstimateSecondOrder(double notional, double principal, double couponRate, DateTime evaluationDate, DateTime maturityDate, double price)
        {
            double T = (maturityDate - evaluationDate).Days / CalendarDaysPerYear;
            double c = couponRate * notional;
            double F = principal;
            double P0 = T * c + F;

            double A = 0;
            for (int t = 1; t <= (int)Math.Round(T); t++) A += t * c;
            A += T * F;

            double B = 0;
            for (int t = 1; t <= (int)Math.Round(T); t++) B += t * (t + 1) * c;
            B += T * (T + 1) * F;

            double discriminant = A * A - 2 * B * (P0 - price);
            double y = 0;
            if (discriminant >= 0)
            {
                y = (A - Math.Sqrt(discriminant)) / B;
                y = Math.Max(0, y);
            }
            return y;
        }

        public static void GraphMSEByDiff(List<Bond> bonds)
        {
            using var writer = new StreamWriter("mse_diff_unbinned.csv");
            writer.WriteLine("CouponMinusYield, ErrorFirstOrder, ErrorEstimate, ErrorSecondOrder");

            foreach (var bond in bonds)
            {
                double diff = bond.CouponRate - bond.Yield;
                double e1 = bond.ErrorFirstOrder * bond.ErrorFirstOrder;
                double e2 = bond.ErrorEstimate * bond.ErrorEstimate;
                double e3 = bond.ErrorSecondOrder * bond.ErrorSecondOrder;
                writer.WriteLine($"{diff:F4}, {e1:F8}, {e2:F8}, {e3:F8}");
            }
        }

        public static void GraphMSEByDaysToExpiry(List<Bond> bonds)
        {
            using var writer = new StreamWriter("mse_days_to_expiry_unbinned.csv");
            writer.WriteLine("DaysToExpiry, ErrorFirstOrder, ErrorEstimate, ErrorSecondOrder");

            foreach (var bond in bonds)
            {
                double daysToExpiry = (bond.MaturityDate - bond.EvaluationDate).TotalDays;
                double e1 = bond.ErrorFirstOrder * bond.ErrorFirstOrder;
                double e2 = bond.ErrorEstimate * bond.ErrorEstimate;
                double e3 = bond.ErrorSecondOrder * bond.ErrorSecondOrder;
                writer.WriteLine($"{daysToExpiry:F2}, {e1:F8}, {e2:F8}, {e3:F8}");
            }
        }

        public static DateTime ToBusinessDay(this DateTime date, bool backwards = true)
        {
            int sign = backwards ? -1 : 1;
            while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
            {
                date = date.AddDays(sign);
            }
            return date;
        }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            DateTime evaluationDate = DateTime.Today;
            double notional = 1000.0;

            double[] couponRates = Enumerable.Range(1, 10).Select(x => x / 100.0).ToArray();
            double[] yields = Enumerable.Range(1, 20).Select(x => x / 100.0).ToArray();
            PaymentFrequency[] frequencies = new[] { PaymentFrequency.Annual, PaymentFrequency.SemiAnnual };

            Console.WriteLine("Generating unique bonds...");
            var bonds = BondUtils.GenerateUniqueBonds(evaluationDate, notional, couponRates, yields, frequencies, maxYears: 30).ToList();
            // finish statement, takes a few seconds to run, need to check some issues with second order.
            Console.WriteLine($"Generated {bonds.Count} bonds.");

            Console.WriteLine("Computing MSE by coupon-yield difference...");
            BondUtils.GraphMSEByDiff(bonds);
            Console.WriteLine("Saved to mse_diff_unbinned.csv");

            Console.WriteLine("Computing MSE by time to expiry...");
            BondUtils.GraphMSEByDaysToExpiry(bonds);
            Console.WriteLine("Saved to mse_days_to_expiry_unbinned.csv");
        }
    }
}
