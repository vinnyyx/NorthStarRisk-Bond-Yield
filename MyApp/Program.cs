﻿using System;
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
        public double YieldCustom;
        public double CouponYield;                 // New: coupon yield estimate
        public double Yield3;
        public double Yield4;
        public double Yield5;
        public double Yield6; // New: Newton-Raphson Yield
        public double ErrorFirstOrder;
        public double ErrorEstimate;
        public double ErrorCustom;
        public double ErrorCouponYield;            // New: error for coupon yield
        public double Error3;
        public double Error4;
        public double Error5;
        public double Error6; // New: Newton-Raphson Yield Error

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


            ErrorFirstOrder = YieldFirstOrder - Yield;
            ErrorEstimate = YieldEstimate - Yield;
            YieldCustom = BondUtils.CustomYieldEstimate(notional, couponRate, freq, evaluationDate, MaturityDate, Price);
            ErrorCustom = YieldCustom - Yield;

            // Compute coupon yield and its error
            CouponYield = BondUtils.CouponSpreadYieldEstimate(notional, couponRate, freq, evaluationDate, MaturityDate, Price);
            ErrorCouponYield = CouponYield - Yield;

            Yield3 = BondUtils.AdaptiveYieldEstimate(notional, couponRate, freq, evaluationDate, MaturityDate, Price, 3 * 365);
            Error3 = Yield3 - Yield;

            Yield4 = BondUtils.AdaptiveYieldEstimate(notional, couponRate, freq, evaluationDate, MaturityDate, Price, 4 * 365);
            Error4 = Yield4 - Yield;

            Yield5 = BondUtils.AdaptiveYieldEstimate(notional, couponRate, freq, evaluationDate, MaturityDate, Price, 5 * 365);
            Error5 = Yield5 - Yield;
            
            // Newton-Raphson method yield and its error
            Yield6 = BondUtils.NewtonRaphsonYieldEstimate(notional, couponRate, freq, evaluationDate, MaturityDate, Price);
            Error6 = Yield6 - Yield;
            
        }
    }

    public static class BondUtils
    {
        public static double CalendarDaysPerYear = 365.25;

        public static IEnumerable<Bond> GenerateUniqueBonds(DateTime evaluationDate, double notional, double[] couponRates,
                                                              double[] yields, PaymentFrequency[] frequencies, int maxYears)
        {
            var seen = new HashSet<(double, double, PaymentFrequency, DateTime)>();
            var uniqueBonds = new List<Bond>();

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
                        uniqueBonds.Add(new Bond(notional, cr, yld, freq, evaluationDate, maturity));
                }
            }

            return uniqueBonds;
        }

        public static DateTime[] GeneratePaymentDates(PaymentFrequency frequency, DateTime evaluationDate, DateTime maturityDate)
        {
            int monthsBetween = 12 / (int)frequency;
            var dates = new List<DateTime>();
            DateTime date = maturityDate;

            while (date > evaluationDate)
            {
                dates.Add(date.ToBusinessDay());
                date = date.AddMonths(-monthsBetween);
            }

            dates.Sort();
            return dates.ToArray();
        }

        public static double[] GenerateCashFlows(double notional, double couponRate, PaymentFrequency frequency,
                                                 DateTime evaluationDate, DateTime maturityDate)
        {
            var dates = GeneratePaymentDates(frequency, evaluationDate, maturityDate);
            int n = dates.Length;
            if (n == 0) return Array.Empty<double>();

            var flows = new double[n];
            for (int i = 0; i < n; i++)
                flows[i] = notional * couponRate / (int)frequency;
            flows[n - 1] += notional;
            return flows;
        }

        public static double PresentValue(double[] cashFlows, DateTime evaluationDate,
                                          DateTime[] paymentDates, double yield)
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

        public static double InitialYieldEstimateFirstOrder(double notional, double principal, double couponRate,
                                                            DateTime evaluationDate, DateTime maturityDate, double price)
        {
            double T = (maturityDate - evaluationDate).Days / CalendarDaysPerYear;
            if (T <= 0 || price <= 0) return 0;
            double c = couponRate * notional;
            double num = principal + T * c - price;
            double den = 0.5 * T * (T + 1) * c + T * principal;
            if (den == 0) return 0;
            return Math.Max(0, num / den);
        }

        public static double InitialYieldEstimate(double notional, double principal, double couponRate,
                                                  DateTime evaluationDate, DateTime maturityDate, double price)
        {
            double T = (maturityDate - evaluationDate).Days / CalendarDaysPerYear;
            double c = couponRate * notional;
            double est = (principal + T * c - price) / (price + (T - 1) * (principal + 0.5 * T * c));
            return Math.Max(0, est);
        }


        public static double CustomYieldEstimate(double notional, double couponRate, PaymentFrequency frequency,
                                                 DateTime evaluationDate, DateTime maturityDate, double price)
        {
            // methodology is interpolation using 10 prices
            var dates = GeneratePaymentDates(frequency, evaluationDate, maturityDate);
            var flows = GenerateCashFlows(notional, couponRate, frequency, evaluationDate, maturityDate);
            double[] yields = { 0.01, 0.04, 0.07 };
            double[] prs   = yields
                             .Select(y => PresentValue(flows, evaluationDate, dates, y))
                             .ToArray();

            for (int i = 0; i < yields.Length; i++)
                if (Math.Abs(price - prs[i]) < 1e-8) return yields[i];

            double Lin(double x0, double x1, double y0, double y1, double px)
                => Math.Abs(x1 - x0) < 1e-8 ? y0 : y0 + (px - x0) * (y1 - y0) / (x1 - x0);

            for (int i = 0; i < prs.Length - 1; i++)
                if ((price >= prs[i] && price <= prs[i + 1]) || (price <= prs[i] && price >= prs[i + 1]))
                    return Lin(prs[i], prs[i + 1], yields[i], yields[i + 1], price);

            return price < prs[0]
                ? Lin(prs[0], prs[1], yields[0], yields[1], price)
                : Lin(prs[1], prs[2], yields[1], yields[2], price);
        }

        public static double CouponSpreadYieldEstimate(double notional, double couponRate, PaymentFrequency frequency,
                                                        DateTime evaluationDate, DateTime maturityDate, double price)
        {
            // calculate annual implied cash flows from coupon + price change
            // implied fair price as denom
            // yield should synthetically be the top divided by bottom
            double T = (maturityDate - evaluationDate).Days / CalendarDaysPerYear;
            if (T <= 0 || price <= 0) return 0;
            double c = couponRate * notional;
            double num = c + (notional - price) / T; // coupon + annualized capital gains from price appreciation
            double den = (notional + price) / 2; // average price 
            if (den == 0) return 0;
            return Math.Max(0, num / den);
        }

        public static double AdaptiveYieldEstimate(
            double notional,
            double couponRate,
            PaymentFrequency frequency,
            DateTime evaluationDate,
            DateTime maturityDate,
            double price,
            double thresholdDays)
        {
            // how many calendar days until maturity
            double daysToExpiry = (maturityDate - evaluationDate).TotalDays;

            if (daysToExpiry <= 0 || price <= 0)
                return 0;

            return daysToExpiry <= thresholdDays
                ? CustomYieldEstimate(notional, couponRate, frequency, evaluationDate, maturityDate, price)
                : CouponSpreadYieldEstimate(notional, couponRate, frequency, evaluationDate, maturityDate, price);
        }

        public static double NewtonRaphsonYieldEstimate(
            double notional, double couponRate, PaymentFrequency frequency,
            DateTime evaluationDate, DateTime maturityDate, double price,
            double initialGuess = 0.05, double tolerance = 1e-8, int maxIter = 100)
        {
            var cashFlows = GenerateCashFlows(notional, couponRate, frequency, evaluationDate, maturityDate);
            var paymentDates = GeneratePaymentDates(frequency, evaluationDate, maturityDate);

            double yield = initialGuess;
            for (int i = 0; i < maxIter; i++)
            {
                double f = PresentValue(cashFlows, evaluationDate, paymentDates, yield) - price;
                
                // Numerical derivative
                double h = 1e-5;
                double df = (PresentValue(cashFlows, evaluationDate, paymentDates, yield + h)
                            - PresentValue(cashFlows, evaluationDate, paymentDates, yield - h)) / (2 * h);

                if (Math.Abs(df) < 1e-12)
                    throw new InvalidOperationException("Derivative is too small. Newton-Raphson failed.");

                double newYield = yield - f / df;

                if (Math.Abs(newYield - yield) < tolerance)
                    return Math.Max(0, newYield);

                yield = newYield;
            }

            throw new InvalidOperationException("Newton-Raphson did not converge.");
        }

        public static void GraphMSEByDiff(List<Bond> bonds)
        {
            using var writer = new StreamWriter("mse_diff_unbinned.csv");
            writer.WriteLine("CouponMinusYield,ErrorFirstOrder,ErrorEstimate,ErrorCustom,ErrorCouponYield,Error3,Error4,Error5,Error6,CouponYield,PaymentFrequency");
            foreach (var b in bonds)
            {
                double diff = b.CouponRate - b.Yield;
                double e1 = b.ErrorFirstOrder;
                double e2 = b.ErrorEstimate;
                double e4 = b.ErrorCustom;
                double e5 = b.ErrorCouponYield;
                double err3 = b.Error3;
                double err4 = b.Error4;
                double err5 = b.Error5;
                double err6 = b.Error6;
                writer.WriteLine($"{diff:F4},{e1:F8},{e2:F8},{e4:F8},{e5:F8},{err3:F8},{err4:F8},{err5:F8},{err6:F15},{b.CouponYield:F8},{b.Frequency}");
            }
        }

        public static void GraphMSEByDaysToExpiry(List<Bond> bonds)
        {
            using var writer = new StreamWriter("mse_days_to_expiry_unbinned.csv");
            writer.WriteLine("DaysToExpiry,ErrorFirstOrder,ErrorEstimate,ErrorCustom,ErrorCouponYield,Error3,Error4,Error5,Error6,CouponYield,PaymentFrequency");
            foreach (var b in bonds)
            {
                double days = (b.MaturityDate - b.EvaluationDate).TotalDays;
                double e1 = b.ErrorFirstOrder;
                double e2 = b.ErrorEstimate;
                double e4 = b.ErrorCustom;
                double e5 = b.ErrorCouponYield;
                double err3 = b.Error3;
                double err4 = b.Error4;
                double err5 = b.Error5;
                double err6 = b.Error6;
                writer.WriteLine($"{days:F2},{e1:F8},{e2:F8},{e4:F8},{e5:F8},{err3:F8},{err4:F8},{err5:F8},{err6:F15},{b.CouponYield:F8},{b.Frequency}");
            }
        }

        public static DateTime ToBusinessDay(this DateTime date, bool backwards = true)
        {
            int sign = backwards ? -1 : 1;
            while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                date = date.AddDays(sign);
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
            PaymentFrequency[] frequencies = { PaymentFrequency.Annual, PaymentFrequency.SemiAnnual };

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
