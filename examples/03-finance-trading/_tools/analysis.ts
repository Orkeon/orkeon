// Analysis tools (EX-01) — the TypeScript port of Orkeon.Trading.Tools'
// Infrastructure/Analysis. Same names, same schemas (translated verbatim from
// the YAML ToolDefinitions), same output shapes. All four are pure functions of
// their input — the C# originals never used Random here, so no PRNG is needed
// and every numeric result is deterministic. Where the C# leaned on MathNet
// (Variance, StandardDeviation) the equivalent helpers from ./math.ts are used.

import { mean, variance, stdDev, returnsFromPrices, correlationMatrix } from "./math.ts";

function isoNow(): string {
    return new Date().toISOString();
}

function num(v: unknown, fallback: number): number {
    return typeof v === "number" && !Number.isNaN(v) ? v : fallback;
}

function round(x: number, digits: number): number {
    const f = 10 ** digits;
    return Math.round(x * f) / f;
}

// One OHLCV bar. Inputs may be plain numbers (close only) or objects with
// snake_case keys; missing high/low/open collapse onto the close.
interface Candle {
    open: number;
    high: number;
    low: number;
    close: number;
    volume: number;
    timestamp?: unknown;
}

function toCandle(p: any): Candle {
    if (typeof p === "number") {
        return { open: p, high: p, low: p, close: p, volume: 0 };
    }
    const close = num(p?.close, 0);
    return {
        open: num(p?.open, close),
        high: num(p?.high, close),
        low: num(p?.low, close),
        close,
        volume: num(p?.volume, 0),
        timestamp: p?.timestamp
    };
}

function toCandles(series: any): Candle[] {
    return Array.isArray(series) ? series.map(toCandle) : [];
}

function closesOf(series: any): number[] {
    return Array.isArray(series)
        ? series.map(p => (typeof p === "number" ? p : num(p?.close, 0)))
        : [];
}

// ---------------------------------------------------------------- correlation

// Beta exactly as the C# computed it: population covariance of the two return
// series over the SAMPLE variance of the benchmark (that mix is inherited).
function betaOf(assetReturns: number[], benchmarkReturns: number[]): number {
    if (assetReturns.length !== benchmarkReturns.length || assetReturns.length < 2) return 1.0;
    let cross = 0;
    for (let i = 0; i < assetReturns.length; i++) cross += assetReturns[i] * benchmarkReturns[i];
    const covariance = cross / assetReturns.length - mean(assetReturns) * mean(benchmarkReturns);
    const benchmarkVariance = variance(benchmarkReturns);
    return benchmarkVariance > 0 ? covariance / benchmarkVariance : 1.0;
}

// Greedy single-pass clustering: each unprocessed symbol absorbs every other
// unprocessed symbol whose absolute correlation clears the threshold.
function correlationClusters(
    symbols: string[],
    matrix: Record<string, Record<string, number>>,
    threshold: number): string[][] {
    const clusters: string[][] = [];
    const processed = new Set<string>();
    for (const symbol of symbols) {
        if (processed.has(symbol)) continue;
        const cluster = [symbol];
        processed.add(symbol);
        for (const other of symbols) {
            if (other === symbol || processed.has(other)) continue;
            if (Math.abs(matrix[symbol]?.[other] ?? 0) >= threshold) {
                cluster.push(other);
                processed.add(other);
            }
        }
        if (cluster.length > 1) clusters.push(cluster);
    }
    return clusters;
}

export const correlationAnalysis = toolBuilder()
    .name("correlation_analysis")
    .description("Analyzes correlations between multiple assets, calculates correlation matrix, beta against benchmark, and provides diversification metrics. Essential for portfolio construction and risk management.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbols: { type: "array", description: "Array of symbols to analyze (e.g., ['AAPL', 'MSFT', 'GOOGL'])" },
                price_data: { type: "object", description: "Dictionary of symbol -> price data arrays" },
                benchmark_symbol: { type: "string", description: "Benchmark symbol for beta calculation (e.g., 'SPY')", default: "SPY" },
                lookback_days: { type: "integer", description: "Number of days to use for correlation calculation", default: 252 }
            },
            required: ["symbols", "price_data"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                symbols: { type: "array" },
                lookback_days: { type: "integer" },
                correlation_matrix: { type: "object" },
                beta_values: { type: "object" },
                benchmark_symbol: { type: "string" },
                diversification_metrics: { type: "object" },
                correlation_clusters: { type: "array" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const symbols: string[] = [...new Set<string>(Array.isArray(input.symbols) ? input.symbols : [])];
        const lookbackDays = num(input.lookback_days, 252);
        const benchmarkSymbol = typeof input.benchmark_symbol === "string" ? input.benchmark_symbol : "SPY";
        const priceData: Record<string, unknown> = input.price_data ?? {};

        const returnsData: Record<string, number[]> = {};
        for (const symbol of symbols) {
            const series = priceData[symbol];
            if (!Array.isArray(series)) continue;
            returnsData[symbol] = returnsFromPrices(closesOf(series.slice(-lookbackDays)));
        }

        const raw = correlationMatrix(symbols, returnsData);
        const matrix: Record<string, Record<string, number>> = {};
        for (const s1 of symbols) {
            matrix[s1] = {};
            for (const s2 of symbols) matrix[s1][s2] = round(raw[s1][s2], 3);
        }

        const betaValues: Record<string, number> = {};
        const benchmarkSeries = priceData[benchmarkSymbol];
        if (Array.isArray(benchmarkSeries)) {
            const benchmarkReturns = returnsFromPrices(closesOf(benchmarkSeries.slice(-lookbackDays)));
            for (const symbol of symbols) {
                if (symbol === benchmarkSymbol || !returnsData[symbol]) continue;
                betaValues[symbol] = betaOf(returnsData[symbol], benchmarkReturns);
            }
        }

        const offDiagonal: number[] = [];
        for (const s1 of symbols) {
            for (const s2 of symbols) {
                if (s1 !== s2) offDiagonal.push(matrix[s1][s2]);
            }
        }
        const avgCorrelation = offDiagonal.length > 0 ? mean(offDiagonal) : 0;
        const diversificationScore = (1 - avgCorrelation) * 100;

        return {
            timestamp: isoNow(),
            symbols,
            lookback_days: lookbackDays,
            correlation_matrix: matrix,
            beta_values: betaValues,
            benchmark_symbol: benchmarkSymbol,
            diversification_metrics: {
                average_correlation: round(avgCorrelation, 3),
                diversification_score: round(diversificationScore, 2),
                diversification_rating: diversificationScore > 70 ? "EXCELLENT" :
                    diversificationScore > 50 ? "GOOD" :
                    diversificationScore > 30 ? "MODERATE" : "POOR"
            },
            correlation_clusters: correlationClusters(symbols, matrix, 0.7)
        };
    })
    .build();

// --------------------------------------------------------------------- regime

function volatilityOf(closes: number[]): number {
    return Math.sqrt(variance(returnsFromPrices(closes)) * 252); // annualized
}

// ADX-like proxy: distance between SMA20 and SMA50 scaled onto 0-100.
function trendStrengthOf(closes: number[]): number {
    const sma20 = mean(closes.slice(-20));
    const sma50 = mean(closes.slice(-Math.min(50, closes.length)));
    const deviation = (Math.abs(sma20 - sma50) / sma50) * 100;
    return Math.min(100, deviation * 10);
}

// Linear regression slope of the closes, normalized onto -100..+100.
function trendDirectionOf(closes: number[]): number {
    const n = closes.length;
    const xMean = (n - 1) / 2;
    const yMean = mean(closes);
    let numerator = 0;
    let denominator = 0;
    for (let i = 0; i < n; i++) {
        numerator += (i - xMean) * (closes[i] - yMean);
        denominator += (i - xMean) * (i - xMean);
    }
    const slope = denominator !== 0 ? numerator / denominator : 0;
    return Math.min(100, Math.max(-100, slope * 1000));
}

function rangeCompressionOf(highs: number[], lows: number[]): number {
    const ranges = highs.map((h, i) => h - lows[i]);
    const avgRange = mean(ranges);
    const recentRange = mean(ranges.slice(-10));
    return avgRange > 0 ? (recentRange / avgRange) * 100 : 100;
}

function determineRegime(
    volatility: number,
    trendStrength: number,
    trendDirection: number,
    rangeCompression: number): { regime: string; confidence: number; probabilities: Record<string, number> } {
    const scores: Record<string, number> = {
        TRENDING_BULL: 0,
        TRENDING_BEAR: 0,
        RANGING: 0,
        VOLATILE: 0
    };
    if (volatility > 0.3) scores.VOLATILE += 40;
    if (trendStrength > 50) {
        if (trendDirection > 10) scores.TRENDING_BULL += 50;
        else if (trendDirection < -10) scores.TRENDING_BEAR += 50;
    }
    if (trendStrength < 30 && volatility < 0.2) scores.RANGING += 50;
    if (rangeCompression < 70) scores.RANGING += 20;

    let regime = "TRENDING_BULL";
    let maxScore = -1;
    let totalScore = 0;
    for (const [key, score] of Object.entries(scores)) {
        totalScore += score;
        if (score > maxScore) { maxScore = score; regime = key; }
    }
    const probabilities: Record<string, number> = {};
    for (const [key, score] of Object.entries(scores)) {
        probabilities[key] = totalScore > 0 ? score / totalScore : 0.25;
    }
    return { regime, confidence: totalScore > 0 ? maxScore / totalScore : 0.5, probabilities };
}

function regimeCharacteristics(regime: string): string[] {
    switch (regime) {
        case "TRENDING_BULL": return [
            "Strong upward momentum", "Higher highs and higher lows",
            "Above key moving averages", "Positive breadth"];
        case "TRENDING_BEAR": return [
            "Strong downward momentum", "Lower highs and lower lows",
            "Below key moving averages", "Negative breadth"];
        case "RANGING": return [
            "Trading within defined range", "Low directional movement",
            "Mean reversion behavior", "Consolidation phase"];
        case "VOLATILE": return [
            "High price swings", "Increased uncertainty",
            "Wider bid-ask spreads", "Unstable support/resistance"];
        default: return ["Unknown regime"];
    }
}

function tradingImplications(regime: string): string[] {
    switch (regime) {
        case "TRENDING_BULL": return [
            "Favor long positions", "Use pullbacks to add exposure",
            "Trailing stops recommended", "Momentum strategies effective"];
        case "TRENDING_BEAR": return [
            "Reduce long exposure", "Consider short positions",
            "Capital preservation priority", "Wait for reversal signals"];
        case "RANGING": return [
            "Mean reversion strategies", "Trade range extremes",
            "Tight stops recommended", "Avoid trend following"];
        case "VOLATILE": return [
            "Reduce position sizes", "Widen stops",
            "Increase cash allocation", "Consider options strategies"];
        default: return ["Standard risk management"];
    }
}

export const marketRegimeClassification = toolBuilder()
    .name("market_regime_classification")
    .description("Classifies current market regime into states: trending bullish, trending bearish, ranging/sideways, or highly volatile. Uses volatility, trend strength, and price action analysis.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "The stock symbol (e.g., 'SPY')" },
                price_data: { type: "array", description: "Array of OHLCV data points (minimum 50 recommended)" },
                lookback_days: { type: "integer", description: "Number of days to analyze for regime classification", default: 60 }
            },
            required: ["symbol", "price_data"]
        },
        output: {
            type: "object",
            properties: {
                symbol: { type: "string" },
                timestamp: { type: "string" },
                current_regime: { type: "string" },
                confidence: { type: "number" },
                regime_probabilities: { type: "object" },
                metrics: { type: "object" },
                characteristics: { type: "array" },
                trading_implications: { type: "array" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const candles = toCandles(input.price_data);
        if (candles.length < 20) throw new Error("At least 20 data points required");
        const lookbackDays = Math.min(candles.length, num(input.lookback_days, 60));
        const window = candles.slice(-lookbackDays);

        const closes = window.map(c => c.close);
        const highs = window.map(c => c.high);
        const lows = window.map(c => c.low);

        const volatility = volatilityOf(closes);
        const trendStrength = trendStrengthOf(closes);
        const trendDirection = trendDirectionOf(closes);
        const rangeCompression = rangeCompressionOf(highs, lows);

        const { regime, confidence, probabilities } = determineRegime(
            volatility, trendStrength, trendDirection, rangeCompression);

        return {
            symbol: input.symbol,
            timestamp: isoNow(),
            current_regime: regime,
            confidence: round(confidence, 2),
            regime_probabilities: probabilities,
            metrics: {
                volatility: round(volatility, 4),
                trend_strength: round(trendStrength, 2),
                trend_direction: round(trendDirection, 2),
                range_compression: round(rangeCompression, 2)
            },
            characteristics: regimeCharacteristics(regime),
            trading_implications: tradingImplications(regime)
        };
    })
    .build();

// ------------------------------------------------------------------- patterns

interface DetectedPattern {
    pattern_name: string;
    type: string;
    confidence: number;
    detection_time: string;
    description: string;
    metadata: Record<string, unknown>;
}

function pattern(name: string, type: string, confidence: number, description: string,
    metadata: Record<string, unknown>): DetectedPattern {
    return {
        pattern_name: name,
        type,
        confidence,
        detection_time: isoNow(),
        description,
        metadata
    };
}

function detectCandlestickPatterns(candles: Candle[]): DetectedPattern[] {
    const patterns: DetectedPattern[] = [];
    if (candles.length < 3) return patterns;

    const recent = candles.slice(-3);
    const last = recent[recent.length - 1];
    const prev = recent.length > 1 ? recent[recent.length - 2] : last;

    const bodySize = Math.abs(last.close - last.open);
    const totalRange = last.high - last.low;
    if (totalRange > 0 && bodySize / totalRange < 0.1) {
        patterns.push(pattern("Doji", "NEUTRAL", 0.85,
            "Indecision in the market, potential reversal", { candle_count: 1 }));
    }

    const lowerShadow = Math.min(last.open, last.close) - last.low;
    const upperShadow = last.high - Math.max(last.open, last.close);
    if (lowerShadow > bodySize * 2 && upperShadow < bodySize * 0.5) {
        patterns.push(pattern("Hammer", "BULLISH", 0.75,
            "Potential bullish reversal after downtrend", { candle_count: 1 }));
    }

    if (recent.length >= 2) {
        const prevBody = Math.abs(prev.close - prev.open);
        const lastBody = Math.abs(last.close - last.open);
        if (prev.close < prev.open && last.close > last.open && lastBody > prevBody) {
            patterns.push(pattern("Bullish Engulfing", "BULLISH", 0.80,
                "Strong bullish reversal signal", { candle_count: 2 }));
        }
        if (prev.close > prev.open && last.close < last.open && lastBody > prevBody) {
            patterns.push(pattern("Bearish Engulfing", "BEARISH", 0.80,
                "Strong bearish reversal signal", { candle_count: 2 }));
        }
    }

    return patterns;
}

// Simplified: flat top (resistance) + rising lows (support).
function isAscendingTriangle(highs: number[], lows: number[]): boolean {
    const maxHigh = Math.max(...highs);
    const recentHighsFlat = highs.slice(5).every(h => Math.abs(h - maxHigh) / maxHigh < 0.02);
    const lowsRising = mean(lows.slice(5)) > mean(lows.slice(0, 5));
    return recentHighsFlat && lowsRising;
}

function detectDoubleBottom(lows: number[]): boolean {
    if (lows.length < 10) return false;
    const minLow = Math.min(...lows);
    const lowIndices: number[] = [];
    lows.forEach((l, i) => {
        if (Math.abs(l - minLow) / minLow < 0.02) lowIndices.push(i);
    });
    return lowIndices.length >= 2 && lowIndices[lowIndices.length - 1] - lowIndices[0] > 5;
}

// Simplified: the three tallest local peaks, middle one highest.
function detectHeadAndShoulders(highs: number[]): boolean {
    if (highs.length < 15) return false;
    const peaks: Array<{ index: number; value: number }> = [];
    for (let i = 1; i < highs.length - 1; i++) {
        if (highs[i] > highs[i - 1] && highs[i] > highs[i + 1]) {
            peaks.push({ index: i, value: highs[i] });
        }
    }
    if (peaks.length < 3) return false;
    const top3 = [...peaks].sort((a, b) => b.value - a.value).slice(0, 3)
        .sort((a, b) => a.index - b.index);
    return top3[1].value > top3[0].value && top3[1].value > top3[2].value;
}

function detectChartPatterns(candles: Candle[]): DetectedPattern[] {
    const patterns: DetectedPattern[] = [];
    if (candles.length < 20) return patterns;

    const highs = candles.map(c => c.high);
    const lows = candles.map(c => c.low);

    if (isAscendingTriangle(highs.slice(-10), lows.slice(-10))) {
        patterns.push(pattern("Ascending Triangle", "BULLISH", 0.70,
            "Bullish continuation pattern, potential breakout upward", { lookback: 10 }));
    }
    if (detectDoubleBottom(lows.slice(-20))) {
        patterns.push(pattern("Double Bottom", "BULLISH", 0.75,
            "Bullish reversal pattern, support level confirmed", { lookback: 20 }));
    }
    if (detectHeadAndShoulders(highs.slice(-30))) {
        patterns.push(pattern("Head and Shoulders", "BEARISH", 0.80,
            "Bearish reversal pattern, potential trend change", { lookback: 30 }));
    }

    return patterns;
}

export const patternRecognition = toolBuilder()
    .name("pattern_recognition")
    .description("Detects technical chart patterns including candlestick patterns (doji, hammer, engulfing), chart patterns (head and shoulders, triangles, flags), and price action setups. Returns detected patterns with confidence scores and trading implications.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "The stock symbol (e.g., 'AAPL')" },
                price_data: { type: "array", description: "Array of OHLCV data points (minimum 50 recommended)" },
                pattern_types: { type: "array", description: "Pattern types to detect: ['candlestick', 'chart', 'all']", default: ["all"] },
                min_confidence: { type: "number", description: "Minimum confidence threshold (0-1)", default: 0.6 }
            },
            required: ["symbol", "price_data"]
        },
        output: {
            type: "object",
            properties: {
                symbol: { type: "string" },
                timestamp: { type: "string" },
                patterns: { type: "array" },
                total_patterns: { type: "integer" },
                pattern_summary: { type: "object" },
                primary_pattern: { type: "object" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const candles = toCandles(input.price_data);
        if (candles.length < 10) throw new Error("At least 10 data points required");
        const patternTypes: string[] = Array.isArray(input.pattern_types) && input.pattern_types.length > 0
            ? input.pattern_types : ["all"];
        const minConfidence = num(input.min_confidence, 0.6);
        const includeAll = patternTypes.includes("all");

        let detected: DetectedPattern[] = [];
        if (includeAll || patternTypes.includes("candlestick")) {
            detected = detected.concat(detectCandlestickPatterns(candles));
        }
        if (includeAll || patternTypes.includes("chart")) {
            detected = detected.concat(detectChartPatterns(candles));
        }
        detected = detected.filter(p => p.confidence >= minConfidence);

        let primary: DetectedPattern | null = null;
        for (const p of detected) {
            if (primary === null || p.confidence > primary.confidence) primary = p;
        }

        return {
            symbol: input.symbol,
            timestamp: isoNow(),
            patterns: detected,
            total_patterns: detected.length,
            pattern_summary: {
                bullish_patterns: detected.filter(p => p.type === "BULLISH").length,
                bearish_patterns: detected.filter(p => p.type === "BEARISH").length,
                neutral_patterns: detected.filter(p => p.type === "NEUTRAL").length
            },
            primary_pattern: primary === null ? null : {
                name: primary.pattern_name,
                type: primary.type,
                confidence: primary.confidence,
                description: primary.description
            }
        };
    })
    .build();

// ----------------------------------------------------------------- indicators

function smaOf(values: number[], period: number): number {
    if (values.length < period) return 0;
    return mean(values.slice(-period));
}

// Classic recursive EMA seeded with the SMA of the first period.
function emaOf(values: number[], period: number): number {
    if (values.length < period) return 0;
    const multiplier = 2 / (period + 1);
    let ema = mean(values.slice(0, period));
    for (let i = period; i < values.length; i++) {
        ema = (values[i] - ema) * multiplier + ema;
    }
    return ema;
}

function trendOf(closes: number[]): string {
    if (closes.length < 50) return "UNKNOWN";
    const sma20 = smaOf(closes, 20);
    const sma50 = smaOf(closes, 50);
    const currentPrice = closes[closes.length - 1];
    if (currentPrice > sma20 && sma20 > sma50) return "BULLISH";
    if (currentPrice < sma20 && sma20 < sma50) return "BEARISH";
    return "SIDEWAYS";
}

function movingAveragesOf(closes: number[]): Record<string, unknown> {
    return {
        sma_20: smaOf(closes, 20),
        sma_50: smaOf(closes, 50),
        sma_100: smaOf(closes, 100),
        sma_200: smaOf(closes, 200),
        ema_12: emaOf(closes, 12),
        ema_26: emaOf(closes, 26),
        ema_50: emaOf(closes, 50),
        ema_200: emaOf(closes, 200),
        trend: trendOf(closes)
    };
}

// Signal line kept as the C# approximation (0.9 * MACD) rather than a true
// 9-period EMA of the MACD series.
function macdOf(closes: number[]): Record<string, unknown> {
    const macdLine = emaOf(closes, 12) - emaOf(closes, 26);
    const signalLine = macdLine * 0.9;
    const histogram = macdLine - signalLine;
    return {
        macd_line: round(macdLine, 2),
        signal_line: round(signalLine, 2),
        histogram: round(histogram, 2),
        signal: histogram > 0 ? "BUY" : histogram < 0 ? "SELL" : "NEUTRAL"
    };
}

// Simple-average RSI over the last 14 changes (no Wilder smoothing), exactly
// like the C# original. Result stays inside 0..100 by construction.
function rsiOf(closes: number[]): Record<string, unknown> {
    if (closes.length < 14) return { value: 50, signal: "NEUTRAL" };
    const changes: number[] = [];
    for (let i = 1; i < closes.length; i++) changes.push(closes[i] - closes[i - 1]);
    const window = changes.slice(-14);
    const positives = window.filter(c => c > 0);
    const negatives = window.filter(c => c < 0);
    const gains = positives.length > 0 ? mean(positives) : 0;
    const losses = Math.abs(negatives.length > 0 ? mean(negatives) : 0);
    const rs = losses === 0 ? 100 : gains / losses;
    const rsi = 100 - 100 / (1 + rs);
    return {
        value: round(rsi, 2),
        signal: rsi < 30 ? "OVERSOLD" : rsi > 70 ? "OVERBOUGHT" : "NEUTRAL"
    };
}

function bollingerOf(closes: number[]): Record<string, unknown> {
    if (closes.length < 20) return {};
    const sma20 = smaOf(closes, 20);
    const sd = stdDev(closes.slice(-20)); // sample std dev, as MathNet did
    const upper = sma20 + 2 * sd;
    const lower = sma20 - 2 * sd;
    const currentPrice = closes[closes.length - 1];
    const percentB = (currentPrice - lower) / (upper - lower);
    return {
        upper: round(upper, 2),
        middle: round(sma20, 2),
        lower: round(lower, 2),
        bandwidth: round(upper - lower, 2),
        percent_b: round(percentB, 2),
        signal: percentB < 0.2 ? "OVERSOLD" : percentB > 0.8 ? "OVERBOUGHT" : "NEUTRAL"
    };
}

// ATR as the plain average of the last 14 true ranges (no Wilder smoothing),
// matching the C# original.
function atrOf(highs: number[], lows: number[], closes: number[]): Record<string, unknown> {
    if (closes.length < 14) return { value: 0 };
    const trueRanges: number[] = [];
    for (let i = 1; i < closes.length; i++) {
        trueRanges.push(Math.max(
            highs[i] - lows[i],
            Math.abs(highs[i] - closes[i - 1]),
            Math.abs(lows[i] - closes[i - 1])));
    }
    const atr14 = mean(trueRanges.slice(-14));
    const percentOfPrice = (atr14 / closes[closes.length - 1]) * 100;
    return {
        value: round(atr14, 2),
        atr_14: round(atr14, 2),
        percent_of_price: round(percentOfPrice, 2),
        volatility_level: percentOfPrice < 2 ? "LOW" : percentOfPrice < 4 ? "NORMAL" :
            percentOfPrice < 6 ? "HIGH" : "EXTREME"
    };
}

// Full Wilder DMI/ADX port: smoothed +DM/-DM/TR, DX series, ADX recursion.
function adxOf(highs: number[], lows: number[], closes: number[], period = 14): Record<string, unknown> {
    if (closes.length < 2 * period) {
        return { value: 0, plus_di: 0, minus_di: 0, trend_strength: "INSUFFICIENT_DATA" };
    }
    const n = closes.length;
    const plusDM = new Array<number>(n).fill(0);
    const minusDM = new Array<number>(n).fill(0);
    const tr = new Array<number>(n).fill(0);
    for (let i = 1; i < n; i++) {
        const upMove = highs[i] - highs[i - 1];
        const downMove = lows[i - 1] - lows[i];
        plusDM[i] = upMove > downMove && upMove > 0 ? upMove : 0;
        minusDM[i] = downMove > upMove && downMove > 0 ? downMove : 0;
        tr[i] = Math.max(
            highs[i] - lows[i],
            Math.abs(highs[i] - closes[i - 1]),
            Math.abs(lows[i] - closes[i - 1]));
    }

    let smoothedPlusDM = 0, smoothedMinusDM = 0, smoothedTR = 0;
    for (let i = 1; i <= period; i++) {
        smoothedPlusDM += plusDM[i];
        smoothedMinusDM += minusDM[i];
        smoothedTR += tr[i];
    }

    const dxValues: number[] = [];
    let plusDI = smoothedTR !== 0 ? (100 * smoothedPlusDM) / smoothedTR : 0;
    let minusDI = smoothedTR !== 0 ? (100 * smoothedMinusDM) / smoothedTR : 0;
    let diSum = plusDI + minusDI;
    dxValues.push(diSum !== 0 ? (Math.abs(plusDI - minusDI) / diSum) * 100 : 0);

    for (let i = period + 1; i < n; i++) {
        smoothedPlusDM = smoothedPlusDM - smoothedPlusDM / period + plusDM[i];
        smoothedMinusDM = smoothedMinusDM - smoothedMinusDM / period + minusDM[i];
        smoothedTR = smoothedTR - smoothedTR / period + tr[i];
        plusDI = smoothedTR !== 0 ? (100 * smoothedPlusDM) / smoothedTR : 0;
        minusDI = smoothedTR !== 0 ? (100 * smoothedMinusDM) / smoothedTR : 0;
        diSum = plusDI + minusDI;
        dxValues.push(diSum !== 0 ? (Math.abs(plusDI - minusDI) / diSum) * 100 : 0);
    }

    let adx: number;
    if (dxValues.length < period) {
        adx = mean(dxValues);
    } else {
        adx = mean(dxValues.slice(0, period));
        for (let i = period; i < dxValues.length; i++) {
            adx = (adx * (period - 1) + dxValues[i]) / period;
        }
    }

    return {
        value: round(adx, 2),
        plus_di: round(plusDI, 2),
        minus_di: round(minusDI, 2),
        trend_strength: adx < 20 ? "WEAK" : adx < 40 ? "MODERATE" : adx < 60 ? "STRONG" : "VERY_STRONG"
    };
}

// %D kept as the C# simplification (0.9 * %K), not a 3-period SMA of %K.
function stochasticOf(highs: number[], lows: number[], closes: number[]): Record<string, unknown> {
    if (closes.length < 14) return {};
    const period = 14;
    const highestHigh = Math.max(...highs.slice(-period));
    const lowestLow = Math.min(...lows.slice(-period));
    const currentClose = closes[closes.length - 1];
    const k = ((currentClose - lowestLow) / (highestHigh - lowestLow)) * 100;
    const d = k * 0.9;
    return {
        k: round(k, 2),
        d: round(d, 2),
        signal: k < 20 ? "OVERSOLD" : k > 80 ? "OVERBOUGHT" : "NEUTRAL"
    };
}

function obvOf(closes: number[], volumes: number[]): Record<string, unknown> {
    let obv = 0;
    for (let i = 1; i < closes.length; i++) {
        if (closes[i] > closes[i - 1]) obv += volumes[i];
        else if (closes[i] < closes[i - 1]) obv -= volumes[i];
    }
    return {
        value: round(obv, 0),
        trend: obv > 0 ? "ACCUMULATION" : "DISTRIBUTION"
    };
}

function vwapOf(candles: Candle[]): Record<string, unknown> {
    let totalPV = 0;
    let totalVolume = 0;
    for (const candle of candles.slice(-20)) {
        const typicalPrice = (candle.high + candle.low + candle.close) / 3;
        totalPV += typicalPrice * candle.volume;
        totalVolume += candle.volume;
    }
    const vwap = totalVolume > 0 ? totalPV / totalVolume : 0;
    const currentPrice = candles[candles.length - 1].close;
    return {
        value: round(vwap, 2),
        // Guarded division (the C# would divide by zero on empty volume)
        deviation_percent: vwap > 0 ? round(((currentPrice - vwap) / vwap) * 100, 2) : 0,
        signal: currentPrice > vwap ? "ABOVE_VWAP" : "BELOW_VWAP"
    };
}

function supportResistanceOf(candles: Candle[]): Record<string, unknown> {
    const last = candles[candles.length - 1];
    const recentHighs = candles.slice(-20).map(c => c.high).sort((a, b) => b - a);
    const recentLows = candles.slice(-20).map(c => c.low).sort((a, b) => a - b);
    return {
        resistance_levels: [round(recentHighs[0], 2), round(recentHighs[1], 2)],
        support_levels: [round(recentLows[0], 2), round(recentLows[1], 2)],
        pivot_point: round((last.high + last.low + last.close) / 3, 2)
    };
}

function signalsOf(indicators: Record<string, any>): Record<string, unknown> {
    const signals: string[] = [];
    let bullishCount = 0;
    let bearishCount = 0;

    const rsiSignal = indicators.rsi?.signal;
    if (rsiSignal === "OVERSOLD") { signals.push("RSI Oversold"); bullishCount++; }
    if (rsiSignal === "OVERBOUGHT") { signals.push("RSI Overbought"); bearishCount++; }

    const macdSignal = indicators.macd?.signal;
    if (macdSignal === "BUY") { signals.push("MACD Bullish"); bullishCount++; }
    if (macdSignal === "SELL") { signals.push("MACD Bearish"); bearishCount++; }

    const bbSignal = indicators.bollinger_bands?.signal;
    if (bbSignal === "OVERSOLD") { signals.push("BB Oversold"); bullishCount++; }
    if (bbSignal === "OVERBOUGHT") { signals.push("BB Overbought"); bearishCount++; }

    return {
        overall_signal: bullishCount > bearishCount ? "BULLISH" :
            bearishCount > bullishCount ? "BEARISH" : "NEUTRAL",
        bullish_signals: bullishCount,
        bearish_signals: bearishCount,
        signal_strength: Math.abs(bullishCount - bearishCount),
        active_signals: signals
    };
}

export const technicalIndicators = toolBuilder()
    .name("technical_indicators")
    .description("Calculates comprehensive technical indicators including moving averages (SMA, EMA), MACD, RSI, Bollinger Bands, ATR, ADX, Stochastic, OBV, VWAP, and support/resistance levels. Returns structured indicator data with signals and interpretations.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "The stock symbol (e.g., 'AAPL')" },
                price_data: { type: "array", description: "Array of OHLCV data points (at least 200 for accurate indicators)" },
                indicators: { type: "array", description: "Specific indicators to calculate (e.g., ['RSI', 'MACD', 'BB']). If empty, calculates all." }
            },
            required: ["symbol", "price_data"]
        },
        output: {
            type: "object",
            properties: {
                symbol: { type: "string" },
                timestamp: { type: "string" },
                current_price: { type: "number" },
                indicators: { type: "object" },
                signals: { type: "object" },
                metadata: { type: "object" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const candles = toCandles(input.price_data);
        if (candles.length < 50) {
            throw new Error("At least 50 data points required for technical indicator calculation");
        }
        const requested: string[] = Array.isArray(input.indicators) ? input.indicators : [];
        const includeAll = requested.length === 0;
        const wants = (...names: string[]) =>
            includeAll || requested.some(i => names.includes(String(i).toUpperCase()));

        const closes = candles.map(c => c.close);
        const highs = candles.map(c => c.high);
        const lows = candles.map(c => c.low);
        const volumes = candles.map(c => c.volume);

        const indicators: Record<string, unknown> = {};
        if (wants("MA", "SMA", "EMA")) indicators.moving_averages = movingAveragesOf(closes);
        if (wants("MACD")) indicators.macd = macdOf(closes);
        if (wants("RSI")) indicators.rsi = rsiOf(closes);
        if (wants("BB", "BOLLINGER")) indicators.bollinger_bands = bollingerOf(closes);
        if (wants("ATR")) indicators.atr = atrOf(highs, lows, closes);
        if (wants("ADX")) indicators.adx = adxOf(highs, lows, closes);
        if (wants("STOCHASTIC", "STOCH")) indicators.stochastic = stochasticOf(highs, lows, closes);
        if (wants("OBV")) indicators.obv = obvOf(closes, volumes);
        if (wants("VWAP")) indicators.vwap = vwapOf(candles);
        if (includeAll || requested.some(i => {
            const u = String(i).toUpperCase();
            return u.includes("SUPPORT") || u.includes("RESISTANCE");
        })) {
            indicators.support_resistance = supportResistanceOf(candles);
        }

        return {
            symbol: input.symbol,
            timestamp: isoNow(),
            current_price: closes[closes.length - 1],
            indicators,
            signals: signalsOf(indicators),
            metadata: {
                data_points: candles.length,
                start_date: candles[0].timestamp ?? null,
                end_date: candles[candles.length - 1].timestamp ?? null
            }
        };
    })
    .build();
