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
        public double YieldCustom;
        public double ErrorFirstOrder;
        public double ErrorEstimate;
        public double ErrorSecondOrder;
        public double ErrorCustom;

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
            YieldCustom = BondUtils.CustomYieldEstimate(notional, couponRate, freq, evaluationDate, MaturityDate, Price);
            ErrorCustom = YieldCustom - Yield;
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

            if (n == 0)
            {
                // Log or skip empty cash flows
                return Array.Empty<double>();
            }

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
            if (T <= 0 || price <= 0) return 0;

            double c = couponRate * notional;
            double numerator = principal + T * c - price;
            double denominator = 0.5 * T * (T + 1) * c + T * principal;

            if (denominator == 0 || double.IsNaN(numerator) || double.IsInfinity(numerator) || double.IsNaN(denominator) || double.IsInfinity(denominator))
                return 0;

            double initialYieldEstimate = numerator / denominator;
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

            if (B == 0 || double.IsNaN(B) || double.IsInfinity(B))
            {
                return InitialYieldEstimateFirstOrder(notional, principal, couponRate, evaluationDate, maturityDate, price);
            }

            double discriminant = A * A - 2 * B * (P0 - price);

            if (double.IsNaN(discriminant) || double.IsInfinity(discriminant))
            {
                return InitialYieldEstimateFirstOrder(notional, principal, couponRate, evaluationDate, maturityDate, price);
            }

            if (discriminant < 0)
            {
                if (discriminant > -1e-10)
                    discriminant = 0;
                else
                    return InitialYieldEstimateFirstOrder(notional, principal, couponRate, evaluationDate, maturityDate, price);
            }

            double sqrtDisc = Math.Sqrt(discriminant);
            double root1 = (A + sqrtDisc) / B;
            double root2 = (A - sqrtDisc) / B;

            double yFirstOrder = InitialYieldEstimateFirstOrder(notional, principal, couponRate, evaluationDate, maturityDate, price);
            double y = (Math.Abs(root1 - yFirstOrder) < Math.Abs(root2 - yFirstOrder)) ? root1 : root2;

            if (double.IsNaN(y) || double.IsInfinity(y))
            {
                return InitialYieldEstimateFirstOrder(notional, principal, couponRate, evaluationDate, maturityDate, price);
            }

            return Math.Max(0, y);
        }

        public static double CustomYieldEstimate(double notional, double couponRate, PaymentFrequency frequency, DateTime evaluationDate, DateTime maturityDate, double price)
        {
            var paymentDates = GeneratePaymentDates(frequency, evaluationDate, maturityDate);
            var cashFlows = GenerateCashFlows(notional, couponRate, frequency, evaluationDate, maturityDate);

            double[] yields = { 0.01, 0.04, 0.07 };
            double[] prices = yields.Select(y => PresentValue(cashFlows, evaluationDate, paymentDates, y)).ToArray();

            for (int i = 0; i < yields.Length; i++)
            {
                if (Math.Abs(price - prices[i]) < 1e-8)
                    return yields[i];
            }

            double LinearEstimate(double x0, double x1, double y0, double y1, double px)
            {
                if (Math.Abs(x1 - x0) < 1e-8) return y0;
                return y0 + (px - x0) * (y1 - y0) / (x1 - x0);
            }

            for (int i = 0; i < yields.Length - 1; i++)
            {
                if ((price >= prices[i] && price <= prices[i + 1]) || (price <= prices[i] && price >= prices[i + 1]))
                {
                    return LinearEstimate(prices[i], prices[i + 1], yields[i], yields[i + 1], price);
                }
            }

            if (price < prices[0])
                return LinearEstimate(prices[0], prices[1], yields[0], yields[1], price);
            else
                return LinearEstimate(prices[1], prices[2], yields[1], yields[2], price);
        }
            public static void GraphMSEByDiff(List<Bond> bonds)
        {
            using var writer = new StreamWriter("mse_diff_unbinned.csv");
            writer.WriteLine("CouponMinusYield, ErrorFirstOrder, ErrorEstimate, ErrorSecondOrder, ErrorCustom, PaymentFrequency");

            foreach (var bond in bonds)
            {
                double diff = bond.CouponRate - bond.Yield;
                double e1 = bond.ErrorFirstOrder * bond.ErrorFirstOrder;
                double e2 = bond.ErrorEstimate * bond.ErrorEstimate;
                double e3 = bond.ErrorSecondOrder * bond.ErrorSecondOrder;
                double e4 = bond.ErrorCustom * bond.ErrorCustom;
                writer.WriteLine($"{diff:F4}, {e1:F8}, {e2:F8}, {e3:F8}, {e4:F8}, {bond.Frequency}");
            }
        }

        public static void GraphMSEByDaysToExpiry(List<Bond> bonds)
        {
            using var writer = new StreamWriter("mse_days_to_expiry_unbinned.csv");
            writer.WriteLine("DaysToExpiry, ErrorFirstOrder, ErrorEstimate, ErrorSecondOrder, ErrorCustom, PaymentFrequency");

            foreach (var bond in bonds)
            {
                double daysToExpiry = (bond.MaturityDate - bond.EvaluationDate).TotalDays;
                double e1 = bond.ErrorFirstOrder * bond.ErrorFirstOrder;
                double e2 = bond.ErrorEstimate * bond.ErrorEstimate;
                double e3 = bond.ErrorSecondOrder * bond.ErrorSecondOrder;
                double e4 = bond.ErrorCustom * bond.ErrorCustom;
                writer.WriteLine($"{daysToExpiry:F2}, {e1:F8}, {e2:F8}, {e3:F8}, {e4:F8}");
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
            Console.WriteLine($"Generated {bonds.Count} bonds.");

            Console.WriteLine("Computing MSE by coupon-yield difference (unbinned)...");
            BondUtils.GraphMSEByDiff(bonds);
            Console.WriteLine("Saved to mse_diff_unbinned.csv");

            Console.WriteLine("Computing MSE by time to expiry (unbinned)...");
            BondUtils.GraphMSEByDaysToExpiry(bonds);
            Console.WriteLine("Saved to mse_days_to_expiry_unbinned.csv");
        }
    }
}
