// Prediction tools (EX-01) — the TypeScript port of Orkeon.Trading.Tools'
// Infrastructure/Prediction. Same names, same schemas (translated verbatim from
// the YAML ToolDefinitions), same output shapes. All four are pure in-memory
// computation — no disk, no network — hence access("read").
//
// Assumed simplifications (documented, deliberate):
// - arima_prediction   → AR(1) around a DAMPED drift on the (optionally
//   differenced) series. auto_params keeps the honest core of the C# grid
//   search: d comes from a split-sample stationarity check, p=1, q=0. An
//   explicit q is accepted and echoed but the MA term is ignored — the C#
//   forecast loop ignored it too ("assumes zero forecast errors").
// - prophet_prediction → naive seasonal decomposition + linear trend:
//   y = trend + seasonality + residual. Weekly/monthly patterns are centered
//   per-bucket means; changepoint_prior_scale blends the global OLS slope with
//   the recent-window slope instead of fitting piecewise segments.
// - ensemble_prediction → weighted combination of the two base engines
//   (AR-drift compounding = "arima", OLS line = "prophet"); "random_forest"
//   and "xgboost" are those same two engines perturbed with small SEEDED
//   Gaussian noise (mulberry32(seedFrom(JSON.stringify(input)))) — same input,
//   same forecast, every run. Weights come from an honest holdout R² (last
//   20% of the series), like the C#. Minimum relaxed from 100 to 60 points.
// - backtesting → honest bar-by-bar loop over the series (equity curve, PnL,
//   drawdown, win rate, transaction costs). The C# re-sized the target
//   position from residual cash on EVERY bar, which churned the book when a
//   signal repeated; here a repeated signal holds the position and only a
//   signal change trades. max_drawdown_pct is a POSITIVE percentage (C# form).
//
// price_data conventions: entries are raw numbers or objects carrying
// close/Close/price/value (+ optional timestamp/date). Series without
// timestamps get synthetic daily UTC dates from a FIXED epoch (2024-01-01) so
// every output — prophet forecast dates included — is deterministic.
// Invariants held everywhere: forecasts are finite; confidence intervals are
// ordered (lower <= point <= upper).

import { mean, variance, stdDev, returnsFromPrices, mulberry32, seedFrom, normalSampler } from "./math.ts";

const DAY_MS = 86400000;
const SYNTHETIC_EPOCH = Date.UTC(2024, 0, 1);
const TREND_DAMP = 0.9; // per-period dampening of the extrapolated drift

function isoNow(): string {
    return new Date().toISOString();
}

function isoDate(d: Date): string {
    return d.toISOString().slice(0, 10);
}

/** Round to n digits; any non-finite intermediate collapses to 0 (finiteness invariant). */
function round(v: number, digits: number): number {
    if (!Number.isFinite(v)) return 0;
    const f = Math.pow(10, digits);
    return Math.round(v * f) / f;
}

function num(v: unknown, fallback: number): number {
    return typeof v === "number" && Number.isFinite(v) ? v : fallback;
}

function bool(v: unknown, fallback: boolean): boolean {
    return typeof v === "boolean" ? v : fallback;
}

function str(v: unknown, fallback: string): string {
    return typeof v === "string" && v.length > 0 ? v : fallback;
}

function clamp(v: number, lo: number, hi: number): number {
    return Math.min(Math.max(v, lo), hi);
}

function clampInt(v: unknown, fallback: number, lo: number, hi: number): number {
    const n = num(v, fallback);
    return clamp(Math.round(n), lo, hi);
}

/** Parse price_data into aligned closes + dates (synthetic fixed-epoch days when absent). */
function toSeries(priceData: unknown): { closes: number[]; dates: Date[] } {
    const closes: number[] = [];
    const dates: Date[] = [];
    if (!Array.isArray(priceData)) return { closes, dates };
    for (const e of priceData as any[]) {
        const c = typeof e === "number" ? e : Number(e?.close ?? e?.Close ?? e?.price ?? e?.value);
        if (!Number.isFinite(c)) continue;
        const raw = typeof e === "object" && e !== null ? (e.timestamp ?? e.date ?? e.Timestamp) : undefined;
        const t = raw !== undefined ? Date.parse(String(raw)) : NaN;
        closes.push(c);
        dates.push(Number.isFinite(t) ? new Date(t) : new Date(SYNTHETIC_EPOCH + (closes.length - 1) * DAY_MS));
    }
    return { closes, dates };
}

/** Lag-k autocorrelation (sample variance denominator, like MathNet). */
function autocorrelation(xs: readonly number[], lag: number): number {
    if (lag >= xs.length) return 0;
    const m = mean(xs);
    const v = variance(xs);
    if (v <= 0) return 0;
    let cov = 0;
    for (let i = 0; i < xs.length - lag; i++) cov += (xs[i] - m) * (xs[i + lag] - m);
    return cov / (xs.length - lag) / v;
}

function diff(xs: readonly number[]): number[] {
    const out: number[] = [];
    for (let i = 1; i < xs.length; i++) out.push(xs[i] - xs[i - 1]);
    return out;
}

function applyDifferencing(xs: readonly number[], d: number): number[] {
    let out = [...xs];
    for (let i = 0; i < d; i++) out = diff(out);
    return out;
}

function zScore(confidenceLevel: number): number {
    if (confidenceLevel >= 0.99) return 2.576;
    if (confidenceLevel >= 0.95) return 1.96;
    if (confidenceLevel >= 0.90) return 1.645;
    return 1.96;
}

/** Split-sample stationarity check — the C# TestStationarity, verbatim semantics. */
function isStationary(xs: readonly number[]): boolean {
    const half = Math.floor(xs.length / 2);
    const a = xs.slice(0, half);
    const b = xs.slice(half);
    const m1 = mean(a), m2 = mean(b);
    const v1 = variance(a), v2 = variance(b);
    const meanDiff = m1 !== 0 ? Math.abs(m1 - m2) / Math.abs(m1) : Math.abs(m2);
    const varDiff = v1 !== 0 ? Math.abs(v1 - v2) / v1 : 0;
    return meanDiff < 0.1 && varDiff < 0.5;
}

/** OLS line over the index axis (x = 0..n-1). */
function fitLine(ys: readonly number[]): { slope: number; intercept: number } {
    const n = ys.length;
    if (n < 2) return { slope: 0, intercept: ys[0] ?? 0 };
    const xm = (n - 1) / 2;
    const ym = mean(ys);
    let numSum = 0, den = 0;
    for (let i = 0; i < n; i++) {
        numSum += (i - xm) * (ys[i] - ym);
        den += (i - xm) * (i - xm);
    }
    const slope = den !== 0 ? numSum / den : 0;
    return { slope, intercept: ym - slope * xm };
}

/** Out-of-sample R², floored at 0 — the C# CalculateR2. */
function r2Score(actual: readonly number[], predicted: readonly number[]): number {
    if (actual.length !== predicted.length || actual.length < 2) return 0;
    const m = mean(actual);
    let ssRes = 0, ssTot = 0;
    for (let i = 0; i < actual.length; i++) {
        ssRes += (actual[i] - predicted[i]) * (actual[i] - predicted[i]);
        ssTot += (actual[i] - m) * (actual[i] - m);
    }
    return ssTot > 0 ? Math.max(0, 1 - ssRes / ssTot) : 0;
}

// ---------------------------------------------------------------------------
// arima_prediction
// ---------------------------------------------------------------------------

export const arimaPrediction = toolBuilder()
    .name("arima_prediction")
    .description("Performs ARIMA (AutoRegressive Integrated Moving Average) time series forecasting. Automatically determines optimal ARIMA(p,d,q) parameters using AIC/BIC criteria. Generates price predictions with confidence intervals. Best for trend-stationary data.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "The stock symbol (e.g., 'AAPL')" },
                price_data: { type: "array", description: "Historical price data (minimum 50 data points recommended)" },
                forecast_periods: { type: "integer", description: "Number of periods ahead to forecast", default: 5 },
                confidence_level: { type: "number", description: "Confidence level for prediction intervals (e.g., 0.95 for 95%)", default: 0.95 },
                auto_params: { type: "boolean", description: "Automatically determine optimal ARIMA(p,d,q) parameters", default: true },
                p: { type: "integer", description: "AR order (if auto_params is false)", default: 1 },
                d: { type: "integer", description: "Differencing order (if auto_params is false)", default: 1 },
                q: { type: "integer", description: "MA order (if auto_params is false)", default: 1 }
            },
            required: ["symbol", "price_data"]
        },
        output: {
            type: "object",
            properties: {
                symbol: { type: "string" },
                timestamp: { type: "string" },
                model_parameters: { type: "object" },
                forecast: { type: "array" },
                model_diagnostics: { type: "object" },
                current_price: { type: "number" },
                forecast_direction: { type: "string" },
                forecast_change_pct: { type: "number" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const { closes } = toSeries(input.price_data);
        if (closes.length < 30) throw new Error("At least 30 data points required for ARIMA");

        const periods = clampInt(input.forecast_periods, 5, 1, 100);
        const confidence = num(input.confidence_level, 0.95);
        const autoParams = bool(input.auto_params, true);

        // Parameter selection — simplified from the C# AIC grid search: the
        // honest core is d (from stationarity) plus an AR(1) term.
        let p: number, d: number, q: number;
        if (autoParams) {
            p = 1;
            d = isStationary(closes) ? 0 : 1;
            q = 0;
        } else {
            p = clampInt(input.p, 1, 0, 3);
            d = clampInt(input.d, 1, 0, 2);
            q = clampInt(input.q, 1, 0, 3); // echoed only — MA term ignored (see header)
        }

        // Fit AR(1) around the drift, in the (differenced) working domain.
        const y = applyDifferencing(closes, d);
        const drift = mean(y);
        const phi = p > 0 ? clamp(autocorrelation(y, 1) * 0.8, -0.95, 0.95) : 0;

        const residuals: number[] = [];
        for (let t = 1; t < y.length; t++) {
            const fitted = drift + phi * (y[t - 1] - drift);
            residuals.push(y[t] - fitted);
        }
        const residStd = stdDev(residuals);

        // Forecast in the working domain: damped drift + geometrically
        // decaying AR(1) deviation (the dampening only makes sense when the
        // domain is an increment, i.e. d >= 1).
        const yF: number[] = [];
        let dev = y.length > 0 ? y[y.length - 1] - drift : 0;
        for (let t = 1; t <= periods; t++) {
            dev *= phi;
            const base = d >= 1 ? drift * Math.pow(TREND_DAMP, t - 1) : drift;
            yF.push(base + dev);
        }

        // Integrate back to the price level.
        const n = closes.length;
        const lastClose = closes[n - 1];
        const pricesF: number[] = [];
        if (d === 0) {
            for (const f of yF) pricesF.push(f);
        } else if (d === 1) {
            let level = lastClose;
            for (const f of yF) { level += f; pricesF.push(level); }
        } else {
            let lastDiff = closes[n - 1] - closes[n - 2];
            let level = lastClose;
            for (const f of yF) { lastDiff += f; level += lastDiff; pricesF.push(level); }
        }

        // Confidence intervals: forecast error grows with the horizon.
        const z = zScore(confidence);
        const forecast = pricesF.map((f, i) => {
            const margin = z * residStd * Math.sqrt(i + 1);
            return {
                period: i + 1,
                predicted_price: round(f, 2),
                confidence_interval_lower: round(f - margin, 2),
                confidence_interval_upper: round(f + margin, 2)
            };
        });

        // Diagnostics (honest values under the simplified fit).
        const nRes = residuals.length;
        let ssRes = 0, absRes = 0, absY = 0;
        for (const r of residuals) { ssRes += r * r; absRes += Math.abs(r); }
        for (const v of y) absY += Math.abs(v);
        const rmse = nRes > 0 ? Math.sqrt(ssRes / nRes) : 0;
        const mapePct = absY > 0 ? (absRes / absY) * 100 : 0;

        let ljungBox = 0;
        const maxLag = Math.min(10, nRes - 1);
        for (let k = 1; k <= maxLag; k++) {
            const rk = autocorrelation(residuals, k);
            ljungBox += (rk * rk) / (nRes - k);
        }
        ljungBox *= nRes * (nRes + 2);

        const k = p + q + 1;
        const sigma2 = nRes > 0 ? ssRes / nRes : 0;
        const logLikelihood = sigma2 > 0
            ? -nRes / 2 * Math.log(2 * Math.PI) - nRes / 2 * Math.log(sigma2) - ssRes / (2 * sigma2)
            : 0;
        const aic = 2 * k - 2 * logLikelihood;

        const lastForecast = pricesF[pricesF.length - 1];
        return {
            symbol: input.symbol,
            timestamp: isoNow(),
            model_parameters: { p, d, q, model_type: `ARIMA(${p},${d},${q})` },
            forecast,
            model_diagnostics: {
                residual_mean: round(mean(residuals), 6),
                residual_std: round(residStd, 4),
                rmse: round(rmse, 4),
                mape_pct: round(mapePct, 2),
                ljung_box_statistic: round(ljungBox, 2),
                ljung_box_interpretation: ljungBox < 20 ? "Residuals appear random (good)" : "Potential autocorrelation in residuals",
                aic: round(aic, 2),
                parameters_used: `ARIMA(${p},${d},${q})`,
                data_points: n
            },
            current_price: round(lastClose, 2),
            forecast_direction: lastForecast > lastClose ? "BULLISH" : "BEARISH",
            forecast_change_pct: round((lastForecast - lastClose) / lastClose * 100, 2)
        };
    })
    .build();

// ---------------------------------------------------------------------------
// prophet_prediction
// ---------------------------------------------------------------------------

/** Centered per-bucket means of a keyed series (day-of-week or month buckets). */
function seasonalPattern(keys: readonly number[], values: readonly number[], size: number): number[] {
    const sums = new Array<number>(size).fill(0);
    const counts = new Array<number>(size).fill(0);
    for (let i = 0; i < keys.length; i++) {
        sums[keys[i]] += values[i];
        counts[keys[i]]++;
    }
    const pattern = sums.map((s, i) => (counts[i] > 0 ? s / counts[i] : 0));
    const pm = mean(pattern);
    return pattern.map(v => v - pm);
}

export const prophetPrediction = toolBuilder()
    .name("prophet_prediction")
    .description("Performs time series forecasting using Facebook Prophet algorithm. Automatically detects seasonality, trend changes, holidays, and irregular patterns. Particularly effective for data with strong seasonal effects and multiple seasonality periods.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "The stock symbol (e.g., 'AAPL')" },
                price_data: { type: "array", description: "Historical price data with timestamps" },
                forecast_periods: { type: "integer", description: "Number of periods ahead to forecast", default: 5 },
                seasonality_mode: { type: "string", description: "Seasonality mode: 'additive' or 'multiplicative'", default: "additive" },
                include_weekly_seasonality: { type: "boolean", description: "Include weekly seasonality component", default: true },
                include_yearly_seasonality: { type: "boolean", description: "Include yearly seasonality component", default: false },
                changepoint_prior_scale: { type: "number", description: "Controls flexibility of trend (0.001-0.5, higher = more flexible)", default: 0.05 }
            },
            required: ["symbol", "price_data"]
        },
        output: {
            type: "object",
            properties: {
                symbol: { type: "string" },
                timestamp: { type: "string" },
                model_config: { type: "object" },
                forecast: { type: "array" },
                components: { type: "object" },
                current_price: { type: "number" },
                forecast_direction: { type: "string" },
                trend_strength: { type: "string" },
                seasonality_strength: { type: "string" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const { closes, dates } = toSeries(input.price_data);
        if (closes.length < 30) throw new Error("At least 30 data points required for Prophet");

        const periods = clampInt(input.forecast_periods, 5, 1, 100);
        const mode = str(input.seasonality_mode, "additive");
        const includeWeekly = bool(input.include_weekly_seasonality, true);
        const includeYearly = bool(input.include_yearly_seasonality, false);
        const changepointPrior = num(input.changepoint_prior_scale, 0.05);

        const n = closes.length;

        // 1. Trend: global OLS line; changepoint_prior_scale blends in the
        // recent-window slope instead of piecewise changepoint fitting.
        const global = fitLine(closes);
        const recentLen = Math.max(10, Math.floor(n / 5));
        const recent = fitLine(closes.slice(n - recentLen));
        const w = clamp(changepointPrior * 10, 0, 1);
        const effSlope = global.slope * (1 - w) + recent.slope * w;
        const trendValues = closes.map((_, i) => global.intercept + global.slope * i);

        // 2. Detrend, then fit centered seasonal patterns (UTC buckets).
        const detrended = closes.map((v, i) => v - trendValues[i]);
        const dayKeys = dates.map(dt => dt.getUTCDay());
        const monthKeys = dates.map(dt => dt.getUTCMonth());
        const weeklyPattern = includeWeekly ? seasonalPattern(dayKeys, detrended, 7) : null;
        const yearlyPattern = includeYearly ? seasonalPattern(monthKeys, detrended, 12) : null;

        const rawSeasonal = (day: number, month: number): number =>
            (weeklyPattern ? weeklyPattern[day] : 0) + (yearlyPattern ? yearlyPattern[month] : 0);
        // Multiplicative mode ported as-is from the C#: exp(s) - 1.
        const seasonalAt = (day: number, month: number): number => {
            const s = rawSeasonal(day, month);
            return mode === "multiplicative" ? Math.exp(s) - 1 : s;
        };

        const seasonalValues = dates.map(dt => seasonalAt(dt.getUTCDay(), dt.getUTCMonth()));

        // 3. Residuals of the decomposition.
        const residuals = detrended.map((v, i) => v - seasonalValues[i]);
        const residStd = stdDev(residuals);

        // 4. Forecast: extrapolated (blended) trend + seasonal at each future date.
        const lastDate = dates[n - 1];
        const trendLast = global.intercept + global.slope * (n - 1);
        const forecast: Record<string, unknown>[] = [];
        for (let i = 0; i < periods; i++) {
            const fDate = new Date(lastDate.getTime() + (i + 1) * DAY_MS);
            const trendF = trendLast + effSlope * (i + 1);
            const seasonalF = seasonalAt(fDate.getUTCDay(), fDate.getUTCMonth());
            const yhat = trendF + seasonalF;
            const intervalWidth = residStd * Math.sqrt(i + 1) * 1.96; // 95% CI
            forecast.push({
                date: isoDate(fDate),
                period: i + 1,
                predicted_price: round(yhat, 2),
                trend: round(trendF, 2),
                seasonality: round(seasonalF, 2),
                confidence_interval_lower: round(yhat - intervalWidth, 2),
                confidence_interval_upper: round(yhat + intervalWidth, 2)
            });
        }

        // 5. Strength labels: slope as % of the mean price per period; the
        // seasonal share of total variance keeps the C# thresholds.
        const relSlopePct = Math.abs(effSlope) / Math.max(mean(closes), 1e-12) * 100;
        const trendStrength =
            relSlopePct > 0.1 ? "STRONG" :
            relSlopePct > 0.05 ? "MODERATE" :
            relSlopePct > 0.01 ? "WEAK" : "FLAT";

        const totalVar = variance(closes);
        const seasonalShare = totalVar > 0 ? variance(seasonalValues) / totalVar : 0;
        const seasonalityStrength =
            seasonalShare > 0.3 ? "STRONG" :
            seasonalShare > 0.15 ? "MODERATE" :
            seasonalShare > 0.05 ? "WEAK" : "MINIMAL";

        const lastPredicted = forecast[forecast.length - 1].predicted_price as number;
        const lastClose = closes[n - 1];
        return {
            symbol: input.symbol,
            timestamp: isoNow(),
            model_config: {
                seasonality_mode: mode,
                weekly_seasonality: includeWeekly,
                yearly_seasonality: includeYearly,
                changepoint_prior_scale: changepointPrior
            },
            forecast,
            components: {
                trend: {
                    slope: round(effSlope, 6),
                    intercept: round(global.intercept, 2),
                    direction: effSlope > 0 ? "UPWARD" : effSlope < 0 ? "DOWNWARD" : "FLAT",
                    // 1 when the recent-slope blend is actually active, else 0
                    changepoints_detected: w > 0 && n > recentLen ? 1 : 0
                },
                seasonality: {
                    mode,
                    weekly_present: weeklyPattern !== null,
                    yearly_present: yearlyPattern !== null,
                    amplitude: seasonalValues.length > 0
                        ? round(Math.max(...seasonalValues) - Math.min(...seasonalValues), 2)
                        : 0
                },
                residuals: {
                    std_deviation: round(residStd, 4),
                    mean: round(mean(residuals), 6)
                }
            },
            current_price: round(lastClose, 2),
            forecast_direction: lastPredicted > lastClose ? "BULLISH" : "BEARISH",
            trend_strength: trendStrength,
            seasonality_strength: seasonalityStrength
        };
    })
    .build();

// ---------------------------------------------------------------------------
// ensemble_prediction
// ---------------------------------------------------------------------------

/** Drift engine: compound the mean simple return ("arima" member of the ensemble). */
function driftForecast(closes: readonly number[], periods: number): number[] {
    const meanReturn = mean(returnsFromPrices(closes));
    const last = closes[closes.length - 1];
    const out: number[] = [];
    for (let i = 0; i < periods; i++) out.push(last * Math.pow(1 + meanReturn, i + 1));
    return out;
}

/** Trend engine: extend the OLS line from the last price ("prophet" member). */
function trendForecast(closes: readonly number[], periods: number): number[] {
    const { slope } = fitLine(closes as number[]);
    const last = closes[closes.length - 1];
    const out: number[] = [];
    for (let i = 0; i < periods; i++) out.push(last + slope * (i + 1));
    return out;
}

const ENSEMBLE_MEMBERS = ["arima", "prophet", "random_forest", "xgboost"] as const;

export const ensemblePrediction = toolBuilder()
    .name("ensemble_prediction")
    .description("Combines multiple prediction models (ARIMA, Prophet, Random Forest, XGBoost) into ensemble forecast. Uses dynamic weighting based on historical accuracy and model confidence. Provides consensus prediction with improved robustness and accuracy.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "The stock symbol (e.g., 'AAPL')" },
                price_data: { type: "array", description: "Historical OHLCV data (minimum 100 data points recommended)" },
                forecast_periods: { type: "integer", description: "Number of periods ahead to forecast", default: 5 },
                models_to_include: { type: "array", description: "Models to include: ['arima', 'prophet', 'random_forest', 'xgboost', 'all']", default: ["all"] },
                weighting_method: { type: "string", description: "Weighting method: 'equal', 'performance', 'inverse_error'", default: "performance" }
            },
            required: ["symbol", "price_data"]
        },
        output: {
            type: "object",
            properties: {
                symbol: { type: "string" },
                timestamp: { type: "string" },
                weighting_method: { type: "string" },
                models_included: { type: "array" },
                ensemble_forecast: { type: "array" },
                individual_forecasts: { type: "object" },
                model_weights: { type: "object" },
                model_agreement: { type: "number" },
                current_price: { type: "number" },
                forecast_direction: { type: "string" },
                consensus_strength: { type: "string" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const { closes } = toSeries(input.price_data);
        // Relaxed from the C#'s 100 — the simplified members are stable from 60 points.
        if (closes.length < 60) throw new Error("At least 60 data points required for ensemble");

        const periods = clampInt(input.forecast_periods, 5, 1, 100);
        const requested: string[] = Array.isArray(input.models_to_include) && input.models_to_include.length > 0
            ? input.models_to_include.map((m: unknown) => String(m))
            : ["all"];
        const includeAll = requested.includes("all");
        const members = ENSEMBLE_MEMBERS.filter(m => includeAll || requested.includes(m));
        if (members.length === 0) throw new Error("No known models in models_to_include");
        const weightingMethod = str(input.weighting_method, "performance");

        // One SEEDED stream per input; members consume it in a FIXED order, so
        // the same input yields the same forecast on every run.
        const gauss = normalSampler(mulberry32(seedFrom(JSON.stringify(input))));
        const noisy = (base: number[]): number[] => base.map(v => v * (1 + gauss() * 0.005));

        const n = closes.length;
        const holdout = Math.max(2, Math.floor(n / 5));
        const train = closes.slice(0, n - holdout);
        const test = closes.slice(n - holdout);

        // Per member: holdout R² on the train slice, then the final forecast.
        const individualForecasts: Record<string, number[]> = {};
        const performance: Record<string, number> = {};
        for (const member of members) {
            const engine = member === "arima" || member === "random_forest" ? driftForecast : trendForecast;
            const perturb = member === "random_forest" || member === "xgboost";
            const holdoutPred = engine(train, holdout);
            const finalPred = engine(closes, periods);
            performance[member] = r2Score(test, perturb ? noisy(holdoutPred) : holdoutPred);
            individualForecasts[member] = perturb ? noisy(finalPred) : finalPred;
        }

        // Weights per method (any weighting degenerates to equal when flat).
        const weights: Record<string, number> = {};
        if (weightingMethod === "equal") {
            for (const m of members) weights[m] = 1 / members.length;
        } else if (weightingMethod === "inverse_error") {
            const inv: Record<string, number> = {};
            let total = 0;
            for (const m of members) { inv[m] = 1 / (1 - performance[m] + 0.01); total += inv[m]; }
            for (const m of members) weights[m] = inv[m] / total;
        } else { // "performance" (default)
            let total = 0;
            for (const m of members) total += performance[m];
            for (const m of members) weights[m] = total > 0 ? performance[m] / total : 1 / members.length;
        }

        // Combine, and derive intervals from the cross-model spread per period.
        const ensembleForecast: number[] = [];
        const stds: number[] = [];
        for (let i = 0; i < periods; i++) {
            let combined = 0;
            for (const m of members) combined += individualForecasts[m][i] * weights[m];
            ensembleForecast.push(combined);
            const predictions = members.map(m => individualForecasts[m][i]);
            stds.push(stdDev(predictions) * Math.sqrt(i + 1));
        }

        // Agreement: 100 - coefficient of variation of first-period predictions.
        const firstPeriod = members.map(m => individualForecasts[m][0]);
        const fpMean = mean(firstPeriod);
        const cv = fpMean !== 0 ? stdDev(firstPeriod) / Math.abs(fpMean) : 0;
        const agreement = Math.max(0, 100 - cv * 100);

        // Consensus: share of members agreeing on the direction vs the last close.
        const lastClose = closes[n - 1];
        const bullish = members.filter(m => individualForecasts[m][periods - 1] > lastClose).length;
        const consensus = Math.max(bullish, members.length - bullish) / members.length;
        const consensusStrength =
            consensus >= 0.9 ? "STRONG" :
            consensus >= 0.7 ? "MODERATE" :
            consensus >= 0.6 ? "WEAK" : "DIVERGENT";

        return {
            symbol: input.symbol,
            timestamp: isoNow(),
            weighting_method: weightingMethod,
            models_included: [...members],
            ensemble_forecast: ensembleForecast.map((f, i) => ({
                period: i + 1,
                predicted_price: round(f, 2),
                confidence_interval_lower: round(f - 1.96 * stds[i], 2),
                confidence_interval_upper: round(f + 1.96 * stds[i], 2),
                prediction_std: round(stds[i], 2)
            })),
            individual_forecasts: Object.fromEntries(members.map(m => [
                m,
                individualForecasts[m].map((f, i) => ({ period: i + 1, predicted_price: round(f, 2) }))
            ])),
            model_weights: Object.fromEntries(members.map(m => [m, round(weights[m], 3)])),
            model_agreement: round(agreement, 1),
            current_price: round(lastClose, 2),
            forecast_direction: ensembleForecast[periods - 1] > lastClose ? "BULLISH" : "BEARISH",
            consensus_strength: consensusStrength
        };
    })
    .build();

// ---------------------------------------------------------------------------
// backtesting
// ---------------------------------------------------------------------------

type BacktestTrade = {
    timestamp: string;
    type: "BUY" | "SELL";
    shares: number;
    price: number;
    commission: number;
    slippage: number;
};

export const backtesting = toolBuilder()
    .name("backtesting")
    .description("Performs vectorized backtesting of trading strategies on historical data. Includes transaction costs, slippage, and realistic execution assumptions. Calculates comprehensive performance metrics: returns, Sharpe ratio, max drawdown, win rate.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "The stock symbol (e.g., 'AAPL')" },
                price_data: { type: "array", description: "Historical OHLCV data for backtesting" },
                strategy_signals: { type: "array", description: "Trading signals: 1 (long), -1 (short), 0 (neutral)" },
                initial_capital: { type: "number", description: "Initial capital for backtesting (default: $100,000)", default: 100000 },
                position_size: { type: "number", description: "Position size as fraction of capital (default: 1.0 = 100%)", default: 1.0 },
                commission_pct: { type: "number", description: "Commission per trade as percentage (default: 0.1%)", default: 0.001 },
                slippage_pct: { type: "number", description: "Slippage per trade as percentage (default: 0.05%)", default: 0.0005 },
                allow_short: { type: "boolean", description: "Allow short selling", default: false }
            },
            required: ["symbol", "price_data", "strategy_signals"]
        },
        output: {
            type: "object",
            properties: {
                symbol: { type: "string" },
                timestamp: { type: "string" },
                backtest_config: { type: "object" },
                performance_metrics: { type: "object" },
                trade_statistics: { type: "object" },
                equity_curve: { type: "array" },
                final_portfolio_value: { type: "number" },
                strategy_quality: { type: "string" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const { closes, dates } = toSeries(input.price_data);
        const n = closes.length;
        if (n < 10) throw new Error("At least 10 data points required for backtesting");
        const signals: number[] = Array.isArray(input.strategy_signals)
            ? input.strategy_signals.map((s: unknown) => Math.sign(Number(s) || 0))
            : [];
        if (signals.length !== n) throw new Error("Strategy signals must match price data length");

        const initialCapital = num(input.initial_capital, 100000);
        const positionSize = num(input.position_size, 1.0);
        const commissionPct = num(input.commission_pct, 0.001);
        const slippagePct = num(input.slippage_pct, 0.0005);
        const allowShort = bool(input.allow_short, false);

        // --- honest bar-by-bar loop ---
        let position = 0; // shares held (negative = short)
        let cashNow = initialCapital;
        const positions: number[] = [];
        const cash: number[] = [initialCapital];
        const portfolioValue: number[] = [initialCapital];
        const trades: BacktestTrade[] = [];

        const executeTrade = (delta: number, price: number, when: Date): void => {
            const tradeValue = Math.abs(delta) * price;
            const commission = tradeValue * commissionPct;
            const slippage = tradeValue * slippagePct;
            if (delta > 0) cashNow -= delta * price + commission + slippage;
            else cashNow += Math.abs(delta) * price - commission - slippage;
            position += delta;
            trades.push({
                timestamp: isoDate(when),
                type: delta > 0 ? "BUY" : "SELL",
                shares: Math.abs(delta),
                price,
                commission,
                slippage
            });
        };

        for (let i = 0; i < n; i++) {
            const price = closes[i];
            const sig = signals[i];
            const wantLong = sig > 0;
            const wantShort = sig < 0 && allowShort;

            // Close a position the signal no longer supports (a repeated
            // signal HOLDS — no per-bar re-sizing churn, unlike the C#).
            if (position > 0 && !wantLong) executeTrade(-position, price, dates[i]);
            else if (position < 0 && !wantShort) executeTrade(-position, price, dates[i]);

            // Open in the signalled direction from the available cash.
            if (position === 0 && (wantLong || wantShort)) {
                const shares = (cashNow * positionSize) / price;
                if (shares > 0) executeTrade(wantLong ? shares : -shares, price, dates[i]);
            }

            positions.push(position);
            cash.push(cashNow);
            portfolioValue.push(cashNow + position * price);
        }

        // --- performance metrics ---
        const finalValue = portfolioValue[portfolioValue.length - 1];
        const totalReturnPct = (finalValue - initialCapital) / initialCapital * 100;

        const dailyReturns: number[] = [];
        for (let i = 1; i < portfolioValue.length; i++) {
            if (portfolioValue[i - 1] !== 0) dailyReturns.push(portfolioValue[i] / portfolioValue[i - 1] - 1);
        }

        const years = portfolioValue.length / 252;
        const annualizedReturn = finalValue > 0 && years > 0
            ? Math.pow(finalValue / initialCapital, 1 / years) - 1
            : -1; // a blown account annualizes to -100%, kept finite
        const annualizedVolatility = stdDev(dailyReturns) * Math.sqrt(252);
        const riskFreeRate = 0.02;
        const sharpeRatio = annualizedVolatility > 0 ? (annualizedReturn - riskFreeRate) / annualizedVolatility : 0;

        const downside = dailyReturns.filter(r => r < 0);
        const downsideDeviation = downside.length > 0
            ? Math.sqrt(mean(downside.map(r => r * r))) * Math.sqrt(252)
            : annualizedVolatility;
        const sortinoRatio = downsideDeviation > 0 ? (annualizedReturn - riskFreeRate) / downsideDeviation : 0;

        // Max drawdown as a POSITIVE fraction of the running peak (C# form).
        let peak = portfolioValue[0];
        let maxDrawdown = 0;
        for (const v of portfolioValue) {
            if (v > peak) peak = v;
            if (peak > 0) maxDrawdown = Math.max(maxDrawdown, (peak - v) / peak);
        }
        const calmarRatio = maxDrawdown > 0 ? annualizedReturn / maxDrawdown : 0;
        const buyHoldPct = closes[0] !== 0 ? (closes[n - 1] - closes[0]) / closes[0] * 100 : 0;

        const performanceMetrics = {
            total_return_pct: round(totalReturnPct, 2),
            annualized_return_pct: round(annualizedReturn * 100, 2),
            annualized_volatility_pct: round(annualizedVolatility * 100, 2),
            sharpe_ratio: round(sharpeRatio, 2),
            sortino_ratio: round(sortinoRatio, 2),
            max_drawdown_pct: round(maxDrawdown * 100, 2),
            calmar_ratio: round(calmarRatio, 2),
            buy_hold_return_pct: round(buyHoldPct, 2),
            excess_return_pct: round(totalReturnPct - buyHoldPct, 2)
        };

        // --- trade statistics (sequential entry/exit pairing) ---
        let tradeStatistics: Record<string, unknown>;
        if (trades.length === 0) {
            tradeStatistics = {
                total_trades: 0,
                total_commission: 0,
                total_slippage: 0,
                note: "No trades executed"
            };
        } else {
            const pnls: number[] = [];
            for (let i = 0; i + 1 < trades.length; i += 2) {
                const entry = trades[i];
                const exit = trades[i + 1];
                const costs = entry.commission + entry.slippage + exit.commission + exit.slippage;
                let pnl = 0;
                if (entry.type === "BUY" && exit.type === "SELL") {
                    pnl = (exit.price - entry.price) * entry.shares - costs; // long round trip
                } else if (entry.type === "SELL" && exit.type === "BUY") {
                    pnl = (entry.price - exit.price) * entry.shares - costs; // short round trip
                }
                pnls.push(pnl);
            }
            const wins = pnls.filter(p => p > 0);
            const losses = pnls.filter(p => p < 0);
            const avgWin = wins.length > 0 ? mean(wins) : 0;
            const avgLoss = losses.length > 0 ? mean(losses) : 0;
            const totalCommission = trades.reduce((s, t) => s + t.commission, 0);
            const totalSlippage = trades.reduce((s, t) => s + t.slippage, 0);
            tradeStatistics = {
                total_trades: trades.length,
                round_trips: pnls.length,
                winning_trades: wins.length,
                losing_trades: losses.length,
                win_rate_pct: pnls.length > 0 ? round(wins.length / pnls.length * 100, 2) : 0,
                average_win: round(avgWin, 2),
                average_loss: round(avgLoss, 2),
                profit_factor: avgLoss !== 0 ? round(Math.abs(avgWin / avgLoss), 2) : 0,
                total_commission: round(totalCommission, 2),
                total_slippage: round(totalSlippage, 2),
                total_transaction_costs: round(totalCommission + totalSlippage, 2),
                transaction_cost_pct_of_capital: round((totalCommission + totalSlippage) / initialCapital * 100, 2)
            };
        }

        // --- equity curve (aligned to the bars; index 0 of pv is pre-trading) ---
        const equityCurve = closes.map((price, i) => ({
            timestamp: isoDate(dates[i]),
            portfolio_value: round(portfolioValue[i + 1], 2),
            cash: round(cash[i + 1], 2),
            position_value: round(positions[i] * price, 2),
            return_pct: round((portfolioValue[i + 1] - initialCapital) / initialCapital * 100, 2)
        }));

        // --- strategy quality score (C# scoring grid, verbatim) ---
        let score = 0;
        if (sharpeRatio > 2.0) score += 3;
        else if (sharpeRatio > 1.0) score += 2;
        else if (sharpeRatio > 0.5) score += 1;
        const maxDDPct = maxDrawdown * 100;
        if (maxDDPct < 10) score += 3;
        else if (maxDDPct < 20) score += 2;
        else if (maxDDPct < 30) score += 1;
        if (totalReturnPct > 50) score += 3;
        else if (totalReturnPct > 20) score += 2;
        else if (totalReturnPct > 0) score += 1;
        const strategyQuality =
            score >= 8 ? "EXCELLENT" :
            score >= 6 ? "GOOD" :
            score >= 4 ? "MODERATE" :
            score >= 2 ? "POOR" : "VERY_POOR";

        return {
            symbol: input.symbol,
            timestamp: isoNow(),
            backtest_config: {
                initial_capital: initialCapital,
                position_size: positionSize,
                commission_pct: commissionPct * 100,
                slippage_pct: slippagePct * 100,
                allow_short: allowShort,
                period_start: isoDate(dates[0]),
                period_end: isoDate(dates[n - 1]),
                trading_days: n
            },
            performance_metrics: performanceMetrics,
            trade_statistics: tradeStatistics,
            equity_curve: equityCurve,
            final_portfolio_value: round(finalValue, 2),
            strategy_quality: strategyQuality
        };
    })
    .build();
