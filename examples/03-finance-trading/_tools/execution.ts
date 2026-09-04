// Execution tools (EX-01) — the TypeScript port of Orkeon.Trading.Tools'
// Infrastructure/Execution. Same names, same schemas (translated verbatim from
// the YAML ToolDefinitions), same output shapes; every Random the C# used is now
// a SEEDED mulberry32 stream (same input, same schedule, same fills), and the
// injected MockVenueDataProvider became an internal per-(symbol, venue) seeded
// generator. Pure mock computation — no disk, no network — hence access("read").

import { mulberry32, seedFrom } from "./math.ts";

function isoNow(): string {
    return new Date().toISOString();
}

/** Round to `digits` decimals (half-up; the C# used banker's rounding). */
function round(v: number, digits: number): number {
    const f = 10 ** digits;
    return Math.round(v * f) / f;
}

/** Parse "HH:mm" (or "HH:mm:ss") into minutes since midnight. */
function parseHm(text: string): number {
    const parts = String(text).split(":").map(Number);
    return (parts[0] || 0) * 60 + (parts[1] || 0) + (parts[2] || 0) / 60;
}

function pad2(v: number): string {
    return String(v).padStart(2, "0");
}

/** TimeSpan "hh\:mm\:ss" formatting of a time expressed in seconds. */
function fmtHms(totalSeconds: number): string {
    const s = Math.floor(totalSeconds);
    return `${pad2(Math.floor(s / 3600))}:${pad2(Math.floor(s / 60) % 60)}:${pad2(s % 60)}`;
}

/** TimeSpan "hh\:mm" formatting of a time expressed in minutes. */
function fmtHm(totalMinutes: number): string {
    const m = Math.floor(totalMinutes);
    return `${pad2(Math.floor(m / 60))}:${pad2(m % 60)}`;
}

export const twapExecution = toolBuilder()
    .name("twap_execution")
    .description("Executes large orders using Time-Weighted Average Price (TWAP) algorithm. Breaks orders into equal-sized slices at regular time intervals. Simple and predictable execution, ideal when volume patterns are uncertain.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "Symbol to execute" },
                side: { type: "string", description: "Order side: 'BUY' or 'SELL'" },
                quantity: { type: "number", description: "Total quantity to execute" },
                start_time: { type: "string", description: "Execution start time (HH:mm)", default: "09:30" },
                end_time: { type: "string", description: "Execution end time (HH:mm)", default: "16:00" },
                slice_interval_minutes: { type: "integer", description: "Time between slices in minutes (default: 5)", default: 5 },
                randomize_timing: { type: "boolean", description: "Randomize slice timing slightly to avoid detection (default: false)", default: false }
            },
            required: ["symbol", "side", "quantity"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                algorithm: { type: "string" },
                symbol: { type: "string" },
                side: { type: "string" },
                total_quantity: { type: "number" },
                start_time: { type: "string" },
                end_time: { type: "string" },
                total_slices: { type: "integer" },
                slice_interval_minutes: { type: "integer" },
                quantity_per_slice: { type: "number" },
                randomized_timing: { type: "boolean" },
                execution_schedule: { type: "array" },
                estimated_completion: { type: "string" },
                execution_strategy: { type: "object" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const quantity = Number(input.quantity);
        const startTime = input.start_time ?? "09:30";
        const endTime = input.end_time ?? "16:00";
        const intervalMinutes = Number(input.slice_interval_minutes ?? 5);
        const randomizeTiming = input.randomize_timing === true;
        const startMin = parseHm(startTime);
        const endMin = parseHm(endTime);
        const durationMin = endMin - startMin;

        const numSlices = Math.floor(durationMin / intervalMinutes);
        const sliceQuantity = Math.round(quantity / numSlices);

        // The C# used new Random(42) for the jitter; the port seeds the stream
        // from the whole input, so a given order always gets the same schedule.
        const rand = randomizeTiming ? mulberry32(seedFrom(JSON.stringify(input))) : null;

        const schedule: any[] = [];
        let cumulative = 0;
        for (let i = 0; i < numSlices; i++) {
            const baseSec = (startMin + i * intervalMinutes) * 60;
            // Random jitter if requested (+/- 2 minutes), clamped to the window.
            const jitterSec = rand ? Math.floor(rand() * 240) - 120 : 0;
            let sliceSec = baseSec + jitterSec;
            if (sliceSec < startMin * 60) sliceSec = startMin * 60;
            if (sliceSec > endMin * 60) sliceSec = endMin * 60;

            // Last slice: allocate the remaining quantity.
            const qty = i === numSlices - 1 ? quantity - cumulative : sliceQuantity;
            cumulative += qty;

            schedule.push({
                slice_number: i + 1,
                execution_time: fmtHms(sliceSec),
                quantity: qty,
                cumulative_quantity: cumulative,
                percentage_of_order: round((qty / quantity) * 100, 2),
                cumulative_percentage: round((cumulative / quantity) * 100, 2)
            });
        }

        return {
            timestamp: isoNow(),
            algorithm: "TWAP",
            symbol: input.symbol,
            side: input.side,
            total_quantity: quantity,
            start_time: startTime,
            end_time: endTime,
            total_slices: schedule.length,
            slice_interval_minutes: intervalMinutes,
            quantity_per_slice: sliceQuantity,
            randomized_timing: randomizeTiming,
            execution_schedule: schedule,
            estimated_completion: `${(durationMin / 60).toFixed(1)} hours`,
            execution_strategy: {
                type: "TWAP",
                objective: "Achieve time-weighted average price",
                benefits: "Simple, predictable, low complexity",
                best_for: "Orders with unpredictable volume patterns or need for steady pace"
            }
        };
    })
    .build();

/** Typical U-shaped intraday volume profile (higher at open and close), normalized to 1. */
function typicalVolumeProfile(): number[] {
    const profile: number[] = [];
    for (let i = 0; i < 12; i++) profile.push(0.025); // open (9:30-10:30): high volume
    for (let i = 0; i < 18; i++) profile.push(0.012); // mid-morning (10:30-12:00)
    for (let i = 0; i < 24; i++) profile.push(0.008); // lunch (12:00-14:00): low volume
    for (let i = 0; i < 18; i++) profile.push(0.010); // afternoon (14:00-15:30)
    for (let i = 0; i < 6; i++) profile.push(0.018);  // close (15:30-16:00): high volume
    const sum = profile.reduce((a, b) => a + b, 0);
    return profile.map(v => v / sum);
}

/** Expected market volume for a 5-minute slice at that time of day (mock, like the C#). */
function estimateMarketVolume(sliceMinutes: number): number {
    const h = sliceMinutes / 60;
    let multiplier = 1.0;
    if (h >= 9.5 && h < 10.5) multiplier = 2.5;       // high volume at open
    else if (h >= 10.5 && h < 12) multiplier = 1.2;   // moderate morning
    else if (h >= 12 && h < 14) multiplier = 0.8;     // lunch slowdown
    else if (h >= 14 && h < 15.5) multiplier = 1.0;   // afternoon pickup
    else if (h >= 15.5 && h <= 16) multiplier = 2.0;  // high volume at close
    const avgDailyVolume = 10_000_000;                // 10M shares/day, liquid stock
    const slicesPerDay = 78;                          // 6.5 hours * 12 five-minute periods
    return (avgDailyVolume / slicesPerDay) * multiplier;
}

export const vwapExecution = toolBuilder()
    .name("vwap_execution")
    .description("Executes large orders using Volume-Weighted Average Price (VWAP) algorithm. Breaks orders into slices matched to intraday volume profile. Minimizes market impact and achieves execution price close to VWAP benchmark.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "Symbol to execute (e.g., 'AAPL')" },
                side: { type: "string", description: "Order side: 'BUY' or 'SELL'" },
                quantity: { type: "number", description: "Total quantity to execute" },
                start_time: { type: "string", description: "Execution start time (HH:mm format, e.g., '09:30')", default: "09:30" },
                end_time: { type: "string", description: "Execution end time (HH:mm format, e.g., '16:00')", default: "16:00" },
                historical_volume_profile: { type: "array", description: "Historical intraday volume profile (optional, will use typical if not provided)" },
                participation_rate: { type: "number", description: "Max participation rate (0-1, default: 0.1 = 10% of market volume)", default: 0.1 }
            },
            required: ["symbol", "side", "quantity"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                algorithm: { type: "string" },
                symbol: { type: "string" },
                side: { type: "string" },
                total_quantity: { type: "number" },
                start_time: { type: "string" },
                end_time: { type: "string" },
                total_slices: { type: "integer" },
                slice_interval_minutes: { type: "number" },
                execution_schedule: { type: "array" },
                estimated_completion: { type: "string" },
                max_participation_rate: { type: "number" },
                execution_strategy: { type: "object" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const quantity = Number(input.quantity);
        const startTime = input.start_time ?? "09:30";
        const endTime = input.end_time ?? "16:00";
        const participationRate = Number(input.participation_rate ?? 0.1);
        const startMin = parseHm(startTime);
        const endMin = parseHm(endTime);
        const durationMin = endMin - startMin;

        const profile: number[] = Array.isArray(input.historical_volume_profile)
            && input.historical_volume_profile.length > 0
            ? input.historical_volume_profile.map(Number)
            : typicalVolumeProfile();

        const sliceIntervalMinutes = 5;
        const numSlices = Math.floor(durationMin / sliceIntervalMinutes);

        const schedule: any[] = [];
        let cumulative = 0;
        for (let i = 0; i < numSlices; i++) {
            const sliceMin = startMin + i * sliceIntervalMinutes;

            // Volume weight for this time slice.
            const profileIndex = Math.min(Math.floor((i / numSlices) * profile.length), profile.length - 1);
            const volumeWeight = profile[profileIndex];

            // Slice quantity, capped by the participation-rate limit.
            let sliceQuantity = Math.round(quantity * volumeWeight);
            const estimatedMarketVolume = estimateMarketVolume(sliceMin);
            sliceQuantity = Math.min(sliceQuantity, estimatedMarketVolume * participationRate);

            if (sliceQuantity > 0) {
                cumulative += sliceQuantity;
                schedule.push({
                    slice_number: schedule.length + 1,
                    execution_time: fmtHm(sliceMin),
                    quantity: sliceQuantity,
                    cumulative_quantity: cumulative,
                    percentage_of_order: round((sliceQuantity / quantity) * 100, 2),
                    cumulative_percentage: round((cumulative / quantity) * 100, 2),
                    volume_weight: round(volumeWeight, 4),
                    estimated_market_volume: Math.round(estimatedMarketVolume),
                    participation_rate: round(sliceQuantity / estimatedMarketVolume, 4)
                });
            }
        }

        // Adjust the last slice so the total quantity is met exactly.
        if (cumulative < quantity && schedule.length > 0) {
            const last = schedule[schedule.length - 1];
            last.quantity += quantity - cumulative;
            last.cumulative_quantity = quantity;
            last.cumulative_percentage = 100;
        }

        return {
            timestamp: isoNow(),
            algorithm: "VWAP",
            symbol: input.symbol,
            side: input.side,
            total_quantity: quantity,
            start_time: startTime,
            end_time: endTime,
            total_slices: schedule.length,
            slice_interval_minutes: sliceIntervalMinutes,
            execution_schedule: schedule,
            estimated_completion: `${(durationMin / 60).toFixed(1)} hours`,
            max_participation_rate: participationRate,
            execution_strategy: {
                type: "VWAP",
                objective: "Match volume-weighted average price",
                benefits: "Low market impact, tracks intraday volume",
                best_for: "Large orders requiring execution over several hours"
            }
        };
    })
    .build();

const defaultVenues = ["NYSE", "NASDAQ", "ARCA", "BATS", "IEX", "EDGX", "EDGA", "PSX"];

interface VenueMarketData {
    venue: string;
    bidPrice: number;
    askPrice: number;
    bidSize: number;
    askSize: number;
    takerFee: number;
    makerRebate: number;
    averageLatencyMs: number;
    historicalFillRate: number;
}

/**
 * Internal port of MockVenueDataProvider: deterministic venue market data,
 * seeded per (symbol, venue) — the C# seeded Random with (symbol+venue).GetHashCode().
 * Same draw order as the original: bid price, ask price, bid size, ask size, fill rate.
 */
function venueMarketData(symbol: string, venue: string): VenueMarketData {
    const rand = mulberry32(seedFrom(symbol + venue));
    return {
        venue,
        bidPrice: 100.00 + (rand() * 0.10 - 0.05),
        askPrice: 100.10 + (rand() * 0.10 - 0.05),
        bidSize: 1000 + Math.floor(rand() * 10000),
        askSize: 1000 + Math.floor(rand() * 10000),
        takerFee: venue === "IEX" ? 0.0009 : 0.003,   // NYSE/NASDAQ/BATS/default: 0.003
        makerRebate: venue === "IEX" ? 0 : 0.0020,    // IEX pays no rebate
        averageLatencyMs: venue === "IEX" ? 2 : 1,
        historicalFillRate: 0.85 + rand() * 0.15
    };
}

function scoreVenue(v: VenueMarketData, objective: string, side: string): number {
    switch (objective.toLowerCase()) {
        case "best_speed":
            return 1.0 / (v.averageLatencyMs + 0.1);
        case "best_fill_rate":
            return v.historicalFillRate * 100;
        case "lowest_cost":
            return 1.0 / ((v.takerFee - v.makerRebate) * 10000 + 0.1);
        case "best_price":
        default: {
            const liquidity = side === "BUY" ? v.askSize : v.bidSize;
            const priceScore = side === "BUY" ? 1.0 / v.askPrice : v.bidPrice;
            const liquidityScore = Math.log(liquidity + 1) / 10.0;
            return priceScore * 0.7 + liquidityScore * 0.3;
        }
    }
}

export const smartOrderRouting = toolBuilder()
    .name("smart_order_routing")
    .description("Routes orders across multiple trading venues to optimize execution quality. Considers liquidity, fees, rebates, latency, and historical fill rates. Dynamically splits orders across venues for best execution.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbol: { type: "string", description: "Symbol to route" },
                side: { type: "string", description: "Order side: 'BUY' or 'SELL'" },
                quantity: { type: "number", description: "Total quantity to route" },
                order_type: { type: "string", description: "Order type: 'MARKET', 'LIMIT', 'IOC', 'FOK'", default: "LIMIT" },
                limit_price: { type: "number", description: "Limit price (required for LIMIT orders)" },
                optimization_objective: { type: "string", description: "Objective: 'best_price', 'best_speed', 'best_fill_rate', 'lowest_cost'", default: "best_price" },
                available_venues: { type: "array", description: "Available trading venues (optional, uses all if not specified)" }
            },
            required: ["symbol", "side", "quantity"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                algorithm: { type: "string" },
                symbol: { type: "string" },
                side: { type: "string" },
                total_quantity: { type: "number" },
                order_type: { type: "string" },
                optimization_objective: { type: "string" },
                total_venues: { type: "integer" },
                routing_plan: { type: "array" },
                expected_costs: { type: "object" },
                execution_strategy: { type: "object" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const quantity = Number(input.quantity);
        const side = String(input.side ?? "");
        const orderType = input.order_type ?? "LIMIT";
        const objective = String(input.optimization_objective ?? "best_price");
        const venues: string[] = Array.isArray(input.available_venues) && input.available_venues.length > 0
            ? input.available_venues.map(String)
            : defaultVenues;

        // Score every venue for the objective, best first (stable sort, like LINQ).
        const scored = venues
            .map(name => {
                const data = venueMarketData(String(input.symbol ?? ""), name);
                return { venue: name, score: scoreVenue(data, objective, side), data };
            })
            .sort((a, b) => b.score - a.score);
        const totalScore = scored.reduce((s, v) => s + v.score, 0);

        // Allocate quantity proportionally to score; venues under 1% are skipped.
        const routing: any[] = [];
        let remaining = quantity;
        for (let i = 0; i < scored.length && remaining > 0; i++) {
            const v = scored[i];
            const allocationPct = v.score / totalScore;
            if (allocationPct < 0.01 && i > 0) continue;

            const allocated = Math.min(Math.round(quantity * allocationPct), remaining);
            if (allocated > 0) {
                routing.push({
                    venue: v.venue,
                    quantity: allocated,
                    priority: i + 1,
                    expectedFillRate: v.data.historicalFillRate,
                    estimatedFee: v.data.takerFee * allocated,
                    estimatedLatencyMs: v.data.averageLatencyMs,
                    venueLiquidity: v.data.askSize + v.data.bidSize,
                    reason: `Score: ${round(v.score, 2)}, ${objective.replace(/_/g, " ")}`
                });
                remaining -= allocated;
            }
        }
        // Any rounding remainder goes to the top venue.
        if (remaining > 0 && routing.length > 0) routing[0].quantity += remaining;

        return {
            timestamp: isoNow(),
            algorithm: "SMART_ORDER_ROUTING",
            symbol: input.symbol,
            side,
            total_quantity: quantity,
            order_type: orderType,
            optimization_objective: objective,
            total_venues: routing.length,
            routing_plan: routing.map(r => ({
                venue: r.venue,
                quantity: r.quantity,
                percentage: round((r.quantity / quantity) * 100, 2),
                priority: r.priority,
                expected_fill_rate: round(r.expectedFillRate * 100, 1),
                estimated_fee: round(r.estimatedFee, 4),
                estimated_latency_ms: r.estimatedLatencyMs,
                venue_liquidity: r.venueLiquidity,
                reason: r.reason
            })),
            expected_costs: {
                total_estimated_fees: round(routing.reduce((s, r) => s + r.estimatedFee, 0), 4),
                average_latency_ms: round(routing.reduce((s, r) => s + r.estimatedLatencyMs, 0) / routing.length, 1),
                expected_fill_rate: round((routing.reduce((s, r) => s + r.expectedFillRate, 0) / routing.length) * 100, 1),
                venues_used: routing.length
            },
            execution_strategy: {
                type: "SMART_ORDER_ROUTING",
                objective: `Optimize for ${objective.replace(/_/g, " ")}`,
                benefits: "Best execution across fragmented markets",
                best_for: "Large orders requiring optimal price discovery"
            },
            limit_price: input.limit_price ?? null
        };
    })
    .build();
