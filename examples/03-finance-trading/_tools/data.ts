// Data tools (EX-01) — the TypeScript port of Orkeon.Trading.Tools'
// Infrastructure/Data category: alternative_data (news / social / analyst
// sentiment, backed by MockSentimentDataProvider) and fundamental_data
// (ratios + composite score, backed by MockFundamentalDataProvider).
// The C# mock providers become internal DETERMINISTIC generators:
// mulberry32(seedFrom(symbol)) replaces Random(symbol.GetHashCode()), so the
// same symbol always yields the same data. All timestamps hang off a single
// "now" captured per execution; sentiment aggregation weighs ages from the
// deterministic offsets, so every numeric output is reproducible.
// Pure mock computation — no disk, no network — hence access("read").

import { mean, mulberry32, seedFrom } from "./math.ts";

/** Integer in [min, max) over a seeded PRNG — the Random.Next(min, max) shape. */
function nextInt(rand: () => number, min: number, max: number): number {
    return min + Math.floor(rand() * (max - min));
}

function round(x: number, digits: number): number {
    const f = 10 ** digits;
    return Math.round(x * f) / f;
}

/** One generated sentiment signal: the output item plus the aggregation inputs. */
interface Signal {
    out: Record<string, unknown>;
    source: string;
    sentiment: number;
    confidence: number;
    positive: number;
    negative: number;
    neutral: number;
    ageHours: number;
}

function confidenceLevel(sentiment: number): string {
    if (sentiment > 0.5) return "HIGH";
    if (sentiment > 0.2 || sentiment < -0.2) return "MEDIUM";
    return "LOW";
}

/** MockSentimentDataProvider.GetNewsSentimentAsync — 3 signals per lookback day. */
function newsSignals(symbol: string, lookbackDays: number, now: number): Signal[] {
    const rand = mulberry32(seedFrom(`${symbol}:news`));
    const signals: Signal[] = [];
    for (let i = 0; i < lookbackDays * 3; i++) {
        const daysAgo = nextInt(rand, 0, lookbackDays);
        const sentiment = round(rand() * 2 - 1, 2);
        const ageHours = daysAgo * 24 + nextInt(rand, 0, 24);
        const mentions = nextInt(rand, 1, 20);
        const positive = sentiment > 0 ? nextInt(rand, 1, 15) : nextInt(rand, 0, 5);
        const negative = sentiment < 0 ? nextInt(rand, 1, 15) : nextInt(rand, 0, 5);
        const neutral = nextInt(rand, 1, 10);
        const confidence = rand() * 0.4 + 0.6;
        signals.push({
            source: "news", sentiment, confidence, positive, negative, neutral, ageHours,
            out: {
                symbol,
                timestamp: new Date(now - ageHours * 3600000).toISOString(),
                source: "news",
                overall_sentiment: sentiment,
                news_sentiment: sentiment,
                mention_count: mentions,
                positive_mentions: positive,
                negative_mentions: negative,
                neutral_mentions: neutral,
                confidence_score: confidence,
                confidence_level: confidenceLevel(sentiment),
                metadata: { simulated: true, article_count: nextInt(rand, 1, 10) }
            }
        });
    }
    return signals;
}

/** MockSentimentDataProvider.GetSocialMediaSentimentAsync — 5 signals per lookback day. */
function socialSignals(symbol: string, lookbackDays: number, now: number): Signal[] {
    const rand = mulberry32(seedFrom(`${symbol}:social`));
    const signals: Signal[] = [];
    for (let i = 0; i < lookbackDays * 5; i++) {
        const daysAgo = nextInt(rand, 0, lookbackDays);
        const sentiment = round(rand() * 2 - 1, 2);
        const ageHours = daysAgo * 24 + nextInt(rand, 0, 24);
        const mentions = nextInt(rand, 10, 1000);
        const positive = sentiment > 0 ? nextInt(rand, 10, 500) : nextInt(rand, 5, 100);
        const negative = sentiment < 0 ? nextInt(rand, 10, 500) : nextInt(rand, 5, 100);
        const neutral = nextInt(rand, 10, 300);
        const confidence = rand() * 0.3 + 0.5;
        signals.push({
            source: "social_media", sentiment, confidence, positive, negative, neutral, ageHours,
            out: {
                symbol,
                timestamp: new Date(now - ageHours * 3600000).toISOString(),
                source: "social_media",
                overall_sentiment: sentiment,
                social_media_sentiment: sentiment,
                mention_count: mentions,
                positive_mentions: positive,
                negative_mentions: negative,
                neutral_mentions: neutral,
                confidence_score: confidence,
                confidence_level: "MEDIUM",
                metadata: {
                    simulated: true,
                    platform: nextInt(rand, 0, 2) === 0 ? "twitter" : "reddit",
                    engagement_score: nextInt(rand, 100, 10000)
                }
            }
        });
    }
    return signals;
}

const RATING_TEXT: Record<number, string> = {
    5: "Strong Buy", 4: "Buy", 3: "Hold", 2: "Sell", 1: "Strong Sell"
};

/** MockSentimentDataProvider.GetAnalystSentimentAsync — about one rating per week. */
function analystSignals(symbol: string, lookbackDays: number, now: number): Signal[] {
    const rand = mulberry32(seedFrom(`${symbol}:analyst`));
    const signals: Signal[] = [];
    const ratingCount = Math.max(1, Math.floor(lookbackDays / 7));
    for (let i = 0; i < ratingCount; i++) {
        const daysAgo = nextInt(rand, 0, lookbackDays);
        const rating = nextInt(rand, 1, 6);
        const sentiment = round((rating - 3) / 2, 2);
        const confidence = rand() * 0.2 + 0.8;
        signals.push({
            source: "analyst", sentiment, confidence,
            positive: rating >= 4 ? 1 : 0,
            negative: rating <= 2 ? 1 : 0,
            neutral: rating === 3 ? 1 : 0,
            ageHours: daysAgo * 24,
            out: {
                symbol,
                timestamp: new Date(now - daysAgo * 86400000).toISOString(),
                source: "analyst",
                overall_sentiment: sentiment,
                analyst_sentiment: sentiment,
                mention_count: 1,
                positive_mentions: rating >= 4 ? 1 : 0,
                negative_mentions: rating <= 2 ? 1 : 0,
                neutral_mentions: rating === 3 ? 1 : 0,
                confidence_score: confidence,
                confidence_level: "HIGH",
                metadata: {
                    simulated: true,
                    rating,
                    rating_text: RATING_TEXT[rating] ?? "Unknown",
                    analyst_firm: `Analyst Firm ${nextInt(rand, 1, 20)}`
                }
            }
        });
    }
    return signals;
}

/** AlternativeDataTool.AggregateSentiment — recency + confidence + source weighting. */
function aggregateSentiment(signals: readonly Signal[]): Record<string, unknown> {
    if (signals.length === 0) return {};

    let totalWeight = 0;
    let weightedSum = 0;
    for (const s of signals) {
        const recency = Math.exp(-s.ageHours / 48);
        const mult = s.source === "analyst" ? 2.0 : s.source === "news" ? 1.5 : 1.0;
        const w = s.confidence * recency * mult;
        totalWeight += w;
        weightedSum += s.sentiment * w;
    }
    const weightedAvg = totalWeight > 0 ? weightedSum / totalWeight : 0;

    const positive = signals.reduce((a, s) => a + s.positive, 0);
    const negative = signals.reduce((a, s) => a + s.negative, 0);
    const neutral = signals.reduce((a, s) => a + s.neutral, 0);
    const total = positive + negative + neutral;

    // POPULATION std dev (divide by N) — the C# helper's choice, unlike math.ts' sample stdDev.
    const m = mean(signals.map(s => s.sentiment));
    const stdDev = Math.sqrt(signals.reduce((a, s) => a + (s.sentiment - m) ** 2, 0) / signals.length);
    const agreement = Math.max(0, 1 - stdDev);
    const confidence = Math.min(1, Math.log10(signals.length + 1) * agreement);

    const bySource = (source: string): Record<string, unknown> => {
        const xs = signals.filter(s => s.source === source);
        if (xs.length === 0) return { count: 0, sentiment: 0, confidence: 0 };
        return {
            count: xs.length,
            sentiment: round(mean(xs.map(x => x.sentiment)), 2),
            confidence: round(mean(xs.map(x => x.confidence)), 2)
        };
    };

    const direction =
        weightedAvg > 0.3 ? "BULLISH" :
        weightedAvg > 0.1 ? "SLIGHTLY_BULLISH" :
        weightedAvg > -0.1 ? "NEUTRAL" :
        weightedAvg > -0.3 ? "SLIGHTLY_BEARISH" : "BEARISH";

    return {
        overall_sentiment: round(weightedAvg, 3),
        sentiment_direction: direction,
        confidence: round(confidence, 2),
        total_signals: signals.length,
        positive_ratio: total > 0 ? round(positive / total, 2) : 0,
        negative_ratio: total > 0 ? round(negative / total, 2) : 0,
        neutral_ratio: total > 0 ? round(neutral / total, 2) : 0,
        sentiment_by_source: {
            news: bySource("news"),
            social_media: bySource("social_media"),
            analyst: bySource("analyst")
        },
        sentiment_std_dev: round(stdDev, 3),
        agreement_score: round(agreement, 2)
    };
}

export const alternativeData = toolBuilder()
    .name("alternative_data")
    .description("Fetches alternative data including news sentiment, social media sentiment, analyst ratings, and other non-traditional market signals. Useful for gauging market sentiment and identifying potential catalysts.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "The stock symbol (e.g., 'AAPL')" },
                data_types: { type: "array", description: "Types of alternative data to fetch: ['news', 'social', 'analyst', 'all']", default: ["all"] },
                lookback_days: { type: "integer", description: "Number of days to look back for data (default: 7, max: 90)", default: 7 },
                aggregate: { type: "boolean", description: "Whether to aggregate sentiment into overall score", default: true }
            },
            required: ["symbol"]
        },
        output: {
            type: "object",
            properties: {
                symbol: { type: "string" },
                timestamp: { type: "string" },
                lookback_days: { type: "integer" },
                data_sources: { type: "array" },
                sentiment_data: { type: "array" },
                total_signals: { type: "integer" },
                aggregated_sentiment: { type: "object" },
                metadata: { type: "object" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const symbol = String(input.symbol ?? "");
        const dataTypes: string[] = Array.isArray(input.data_types) && input.data_types.length > 0
            ? input.data_types : ["all"];
        const lookbackDays = typeof input.lookback_days === "number" ? input.lookback_days : 7;
        const aggregate = input.aggregate !== false;
        if (lookbackDays > 90) throw new Error("lookback_days must not exceed 90");

        const now = Date.now();
        const all = dataTypes.includes("all");
        const signals: Signal[] = [];
        if (all || dataTypes.includes("news")) signals.push(...newsSignals(symbol, lookbackDays, now));
        if (all || dataTypes.includes("social")) signals.push(...socialSignals(symbol, lookbackDays, now));
        if (all || dataTypes.includes("analyst")) signals.push(...analystSignals(symbol, lookbackDays, now));

        return {
            symbol,
            timestamp: new Date(now).toISOString(),
            lookback_days: lookbackDays,
            data_sources: dataTypes,
            sentiment_data: signals.map(s => s.out),
            total_signals: signals.length,
            aggregated_sentiment: aggregate && signals.length > 0 ? aggregateSentiment(signals) : {},
            metadata: {
                news_count: signals.filter(s => s.source === "news").length,
                social_count: signals.filter(s => s.source === "social_media").length,
                analyst_count: signals.filter(s => s.source === "analyst").length
            }
        };
    })
    .build();

// --- fundamental_data ------------------------------------------------------

/** The MockFundamentalDataProvider ranges, drawn in the exact C# order. */
function generateFundamentals(symbol: string): Record<string, number> {
    const rand = mulberry32(seedFrom(symbol));
    return {
        market_cap: 2500000000000 + rand() * 500000000000,
        enterprise_value: 2400000000000 + rand() * 500000000000,
        pe_ratio: 25 + rand() * 15,
        peg_ratio: 1.5 + rand() * 1,
        price_to_book: 8 + rand() * 10,
        price_to_sales: 6 + rand() * 4,
        ev_to_ebitda: 18 + rand() * 8,
        roe: 25 + rand() * 25,
        roa: 15 + rand() * 15,
        roi: 18 + rand() * 18,
        net_margin: 20 + rand() * 15,
        operating_margin: 25 + rand() * 15,
        gross_margin: 40 + rand() * 20,
        current_ratio: 1.2 + rand() * 1,
        quick_ratio: 1.0 + rand() * 0.8,
        debt_to_equity: 0.5 + rand() * 1,
        interest_coverage: 10 + rand() * 20,
        revenue_growth_yoy: 10 + rand() * 20,
        earnings_growth_yoy: 15 + rand() * 25,
        dividend_yield: 0.5 + rand() * 2
    };
}

function valuationScore(d: Record<string, number>): number {
    let score = 50;
    if (d.pe_ratio < 15) score += 15; else if (d.pe_ratio < 25) score += 10; else if (d.pe_ratio < 35) score += 5;
    if (d.peg_ratio < 1) score += 15; else if (d.peg_ratio < 1.5) score += 10; else if (d.peg_ratio < 2) score += 5;
    if (d.price_to_book < 3) score += 10; else if (d.price_to_book < 5) score += 5;
    return Math.min(100, Math.max(0, score));
}

function profitabilityScore(d: Record<string, number>): number {
    let score = 0;
    if (d.roe > 20) score += 20; else if (d.roe > 15) score += 15; else if (d.roe > 10) score += 10;
    if (d.roa > 10) score += 15; else if (d.roa > 5) score += 10; else if (d.roa > 2) score += 5;
    if (d.net_margin > 20) score += 20; else if (d.net_margin > 10) score += 15; else if (d.net_margin > 5) score += 10;
    if (d.operating_margin > 20) score += 15; else if (d.operating_margin > 10) score += 10;
    return Math.min(100, Math.max(0, score));
}

function healthScore(d: Record<string, number>): number {
    let score = 0;
    if (d.current_ratio > 2) score += 25; else if (d.current_ratio > 1.5) score += 20; else if (d.current_ratio > 1) score += 15;
    if (d.debt_to_equity < 0.5) score += 30; else if (d.debt_to_equity < 1) score += 20; else if (d.debt_to_equity < 2) score += 10;
    if (d.interest_coverage > 10) score += 25; else if (d.interest_coverage > 5) score += 20; else if (d.interest_coverage > 2) score += 10;
    return Math.min(100, Math.max(0, score));
}

function growthScore(d: Record<string, number>): number {
    let score = 0;
    if (d.revenue_growth_yoy > 20) score += 30; else if (d.revenue_growth_yoy > 10) score += 20; else if (d.revenue_growth_yoy > 5) score += 10;
    if (d.earnings_growth_yoy > 25) score += 30; else if (d.earnings_growth_yoy > 15) score += 20; else if (d.earnings_growth_yoy > 5) score += 10;
    return Math.min(100, Math.max(0, score));
}

export const fundamentalData = toolBuilder()
    .name("fundamental_data")
    .description("Fetches fundamental data for companies including financial ratios, valuation metrics, profitability indicators, growth rates, and financial health metrics. Data sourced from financial statements and market data.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "The stock symbol (e.g., 'AAPL')" },
                metrics: { type: "array", description: "Specific metrics to fetch (e.g., ['PE', 'ROE', 'DebtToEquity']). If empty, fetches all." },
                period: { type: "string", description: "Reporting period: 'annual', 'quarterly', 'ttm' (trailing twelve months)", default: "ttm" }
            },
            required: ["symbol"]
        },
        output: {
            type: "object",
            properties: {
                symbol: { type: "string" },
                report_date: { type: "string" },
                valuation: { type: "object" },
                profitability: { type: "object" },
                financial_health: { type: "object" },
                growth: { type: "object" },
                overall_score: { type: "number" },
                score_breakdown: { type: "object" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        // Like the C# original, `metrics` is accepted but never filters the output,
        // and `period` only selects the (identical) mock generation path.
        const symbol = String(input.symbol ?? "");
        const d = generateFundamentals(symbol);

        const valuation = valuationScore(d);
        const profitability = profitabilityScore(d);
        const health = healthScore(d);
        const growth = growthScore(d);

        return {
            symbol,
            report_date: new Date(Date.now() - 30 * 86400000).toISOString(),
            valuation: {
                market_cap: d.market_cap, enterprise_value: d.enterprise_value,
                pe_ratio: d.pe_ratio, peg_ratio: d.peg_ratio,
                price_to_book: d.price_to_book, price_to_sales: d.price_to_sales,
                ev_to_ebitda: d.ev_to_ebitda
            },
            profitability: {
                roe: d.roe, roa: d.roa, roi: d.roi,
                net_margin: d.net_margin, operating_margin: d.operating_margin,
                gross_margin: d.gross_margin
            },
            financial_health: {
                current_ratio: d.current_ratio, quick_ratio: d.quick_ratio,
                debt_to_equity: d.debt_to_equity, interest_coverage: d.interest_coverage
            },
            growth: {
                revenue_growth_yoy: d.revenue_growth_yoy,
                earnings_growth_yoy: d.earnings_growth_yoy,
                dividend_yield: d.dividend_yield
            },
            overall_score: valuation * 0.25 + profitability * 0.30 + health * 0.25 + growth * 0.20,
            score_breakdown: {
                valuation_score: valuation,
                profitability_score: profitability,
                health_score: health,
                growth_score: growth
            }
        };
    })
    .build();
