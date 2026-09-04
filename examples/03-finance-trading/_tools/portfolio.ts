// Portfolio tools (EX-01) — the TypeScript port of Orkeon.Trading.Tools'
// Infrastructure/Portfolio. Same names, same schemas (translated verbatim from
// the YAML ToolDefinitions), same snake_case output shapes. All five are pure
// mock computation — no disk, no network — hence access("read").
//
// DELIBERATE NUMERICAL SIMPLIFICATIONS (the C# already ran on mock data and
// itself avoided real solvers — random search, gradient nudges, proportional
// allocations). This port keeps the same honesty budget but makes every tool
// DETERMINISTIC: where the C# used Random(42..45), the PRNG here is
// mulberry32 seeded from the input itself, so the same input always yields the
// same portfolio. Each tool documents its own shortcut below. The invariants
// preserved everywhere: weights >= 0, weights sum to exactly 1 (largest-weight
// residual fix after 4-decimal rounding), portfolio volatility computed via
// the shared portfolioVolatility (sqrt(w' S w)), Sharpe = (ret - rf) / vol
// guarded to 0 on zero volatility.

import {
    mean, correlationMatrix, covarianceMatrix, portfolioVolatility,
    mulberry32, seedFrom, type Matrix
} from "./math.ts";

// ---------------------------------------------------------------- shared bits

function isoNow(): string {
    return new Date().toISOString();
}

function num(v: unknown, fallback: number): number {
    return typeof v === "number" && !Number.isNaN(v) ? v : fallback;
}

function round(x: number, d: number): number {
    const f = Math.pow(10, d);
    return Math.round(x * f) / f;
}

/** Distinct symbols, first occurrence wins — the C# called .Distinct(). */
function dedupe(xs: unknown): string[] {
    const seen: Record<string, boolean> = {};
    const out: string[] = [];
    for (const x of (Array.isArray(xs) ? xs : [])) {
        const s = String(x);
        if (!seen[s]) { seen[s] = true; out.push(s); }
    }
    return out;
}

function expectedReturnsOf(
    symbols: readonly string[],
    returnsData: Record<string, readonly number[]>): Record<string, number> {
    const out: Record<string, number> = {};
    for (const s of symbols) {
        const r = returnsData[s];
        out[s] = Array.isArray(r) ? mean(r) : 0;
    }
    return out;
}

function portfolioReturn(
    weights: Record<string, number>,
    expectedReturns: Record<string, number>): number {
    let r = 0;
    for (const k of Object.keys(weights)) r += weights[k] * (expectedReturns[k] ?? 0);
    return r;
}

/**
 * Round weights to 4 decimals AND keep the sum-to-1 invariant exact: the
 * rounding residual (a multiple of 1e-4) is folded into the largest weight.
 * The C# rounded each weight independently and let the sum drift by up to a
 * few 1e-4 — this port refuses that drift.
 */
function roundWeightsSummingToOne(weights: Record<string, number>): Record<string, number> {
    const out: Record<string, number> = {};
    let sum = 0;
    let largestKey = "";
    let largestVal = -Infinity;
    for (const k of Object.keys(weights)) {
        const r = round(weights[k], 4);
        out[k] = r;
        sum += r;
        if (weights[k] > largestVal) { largestVal = weights[k]; largestKey = k; }
    }
    if (largestKey !== "") out[largestKey] = round(out[largestKey] + (1 - sum), 4);
    return out;
}

/** Random long-only weights: normalize, clamp to bounds, renormalize (C# parity). */
function randomWeights(
    symbols: readonly string[],
    minWeight: number,
    maxWeight: number,
    rand: () => number): Record<string, number> {
    const raw = symbols.map(() => rand());
    let sum = 0;
    for (const r of raw) sum += r;
    const weights: Record<string, number> = {};
    let total = 0;
    for (let i = 0; i < symbols.length; i++) {
        const w = Math.max(minWeight, Math.min(maxWeight, raw[i] / (sum || 1)));
        weights[symbols[i]] = w;
        total += w;
    }
    if (total > 0) for (const s of symbols) weights[s] /= total;
    return weights;
}

/** Diversification Ratio = (sum w_i * sigma_i) / sigma_p, rounded to 2. */
function diversificationRatio(
    weights: Record<string, number>,
    cov: Matrix): number {
    let weightedVols = 0;
    for (const k of Object.keys(weights)) {
        weightedVols += weights[k] * Math.sqrt(Math.max(0, cov[k]?.[k] ?? 0));
    }
    const vol = portfolioVolatility(weights, cov);
    return vol > 0 ? round(weightedVols / vol, 2) : 1;
}

function scaleWeightsToPct(weights: Record<string, number>): Record<string, number> {
    const out: Record<string, number> = {};
    for (const k of Object.keys(weights)) out[k] = round(weights[k] * 100, 2);
    return out;
}

// ---------------------------------------------- mean_variance_optimization

interface PoolEntry {
    weights: Record<string, number>;
    ret: number;
    vol: number;
    sharpe: number;
}

export const meanVarianceOptimization = toolBuilder()
    .name("mean_variance_optimization")
    .description("Performs Mean-Variance Optimization using Markowitz Modern Portfolio Theory. Constructs efficient frontier, finds maximum Sharpe ratio portfolio, minimum variance portfolio. Supports constraints: long-only, weight bounds, sector limits, turnover constraints.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbols: { type: "array", description: "Array of symbols to optimize (e.g., ['AAPL', 'MSFT', 'GOOGL'])" },
                returns_data: { type: "object", description: "Dictionary of symbol -> historical returns array" },
                risk_free_rate: { type: "number", description: "Risk-free rate for Sharpe ratio calculation (default: 0.02 = 2%)", default: 0.02 },
                target_return: { type: "number", description: "Target return for efficient portfolio (optional)" },
                target_volatility: { type: "number", description: "Target volatility for efficient portfolio (optional)" },
                min_weight: { type: "number", description: "Minimum weight per asset (default: 0 for long-only)", default: 0.0 },
                max_weight: { type: "number", description: "Maximum weight per asset", default: 1.0 },
                current_weights: { type: "object", description: "Current portfolio weights for turnover constraint" },
                max_turnover: { type: "number", description: "Maximum portfolio turnover (optional)" }
            },
            required: ["symbols", "returns_data"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                method: { type: "string" },
                symbols: { type: "array" },
                optimal_weights: { type: "object" },
                expected_return: { type: "number" },
                expected_volatility: { type: "number" },
                sharpe_ratio: { type: "number" },
                risk_free_rate: { type: "number" },
                alternative_portfolios: { type: "object" },
                efficient_frontier: { type: "array" },
                diversification_metrics: { type: "object" },
                constraints_applied: { type: "array" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const symbols = dedupe(input.symbols);
        if (symbols.length < 2) throw new Error("At least 2 symbols required for optimization");

        const returnsData = input.returns_data ?? {};
        const riskFreeRate = num(input.risk_free_rate, 0.02);
        const targetReturn = typeof input.target_return === "number" ? input.target_return : null;
        const targetVolatility = typeof input.target_volatility === "number" ? input.target_volatility : null;
        const minWeight = num(input.min_weight, 0.0);
        const maxWeight = num(input.max_weight, 1.0);
        const currentWeights = input.current_weights ?? null;
        const maxTurnover = typeof input.max_turnover === "number" ? input.max_turnover : null;

        const expected = expectedReturnsOf(symbols, returnsData);
        const cov = covarianceMatrix(symbols, returnsData);

        // SIMPLIFICATION: no quadratic-programming solver. The C# ran four
        // separate 10,000-draw random searches (seeds 42-45); this port draws
        // ONE seeded pool of 1,000 feasible portfolios (seed derived from the
        // input, so deterministic) and answers every query — max Sharpe, min
        // variance, target return/volatility, efficient frontier — by scanning
        // that same pool.
        const rand = mulberry32(seedFrom(JSON.stringify(input)));
        const pool: PoolEntry[] = [];
        for (let i = 0; i < 1000; i++) {
            const w = randomWeights(symbols, minWeight, maxWeight, rand);
            const ret = portfolioReturn(w, expected);
            const vol = portfolioVolatility(w, cov);
            pool.push({ weights: w, ret, vol, sharpe: vol > 0 ? (ret - riskFreeRate) / vol : -Infinity });
        }

        let maxSharpeP = pool[0];
        let minVarP = pool[0];
        for (const p of pool) {
            if (p.sharpe > maxSharpeP.sharpe) maxSharpeP = p;
            if (p.vol < minVarP.vol) minVarP = p;
        }

        // Target portfolio: within 0.5% of the target, best on the other axis;
        // falls back to the max-Sharpe portfolio when nothing qualifies (C# parity).
        let targetP: PoolEntry | null = null;
        if (targetReturn !== null) {
            for (const p of pool) {
                if (Math.abs(p.ret - targetReturn) < 0.005 && (targetP === null || p.vol < targetP.vol)) targetP = p;
            }
            if (targetP === null) targetP = maxSharpeP;
        } else if (targetVolatility !== null) {
            for (const p of pool) {
                if (Math.abs(p.vol - targetVolatility) < 0.005 && (targetP === null || p.ret > targetP.ret)) targetP = p;
            }
            if (targetP === null) targetP = maxSharpeP;
        }

        let optimal: Record<string, number> = { ...(targetP ?? maxSharpeP).weights };
        if (currentWeights !== null && maxTurnover !== null) {
            optimal = applyTurnoverConstraint(optimal, currentWeights, maxTurnover);
        }

        const pRet = portfolioReturn(optimal, expected);
        const pVol = portfolioVolatility(optimal, cov);
        const sharpe = pVol > 0 ? (pRet - riskFreeRate) / pVol : 0;

        // Efficient frontier: 20 target returns spanning the asset means,
        // each answered from the shared pool (the C# reran the random search
        // per point). Sharpe here uses the request's risk-free rate — the C#
        // hardcoded 0.02 inside the frontier loop.
        const means = symbols.map(s => expected[s]);
        let minRet = means[0], maxRet = means[0];
        for (const m of means) { if (m < minRet) minRet = m; if (m > maxRet) maxRet = m; }
        const frontier: { ret: number; vol: number; sharpe: number }[] = [];
        const numPoints = 20;
        for (let i = 0; i < numPoints; i++) {
            const t = minRet + (maxRet - minRet) * i / (numPoints - 1);
            let best: PoolEntry | null = null;
            for (const p of pool) {
                if (Math.abs(p.ret - t) < 0.005 && (best === null || p.vol < best.vol)) best = p;
            }
            const chosen = best ?? maxSharpeP;
            frontier.push({
                ret: chosen.ret,
                vol: chosen.vol,
                sharpe: chosen.vol > 0 ? (chosen.ret - riskFreeRate) / chosen.vol : 0
            });
        }
        frontier.sort((a, b) => a.vol - b.vol);

        // Diversification metrics (inverse Herfindahl, concentration).
        let herfindahl = 0, maxW = 0;
        const sortedW = Object.keys(optimal).map(k => optimal[k]).sort((a, b) => b - a);
        for (const w of sortedW) { herfindahl += w * w; if (w > maxW) maxW = w; }
        const effectiveN = herfindahl > 0 ? 1 / herfindahl : sortedW.length;
        let top5 = 0;
        for (let i = 0; i < Math.min(5, sortedW.length); i++) top5 += sortedW[i];

        const constraints: string[] = [
            `Weight bounds: [${minWeight}, ${maxWeight}]`,
            "Sum of weights = 1.0"
        ];
        if (currentWeights !== null && maxTurnover !== null) constraints.push(`Max turnover: ${maxTurnover}`);

        return {
            timestamp: isoNow(),
            method: "MEAN_VARIANCE",
            symbols,
            optimal_weights: roundWeightsSummingToOne(optimal),
            expected_return: round(pRet * 100, 2),
            expected_volatility: round(pVol * 100, 2),
            sharpe_ratio: round(sharpe, 2),
            risk_free_rate: riskFreeRate,
            alternative_portfolios: {
                maximum_sharpe: {
                    weights: roundWeightsSummingToOne(maxSharpeP.weights),
                    expected_return: round(maxSharpeP.ret * 100, 2),
                    expected_volatility: round(maxSharpeP.vol * 100, 2)
                },
                minimum_variance: {
                    weights: roundWeightsSummingToOne(minVarP.weights),
                    expected_return: round(minVarP.ret * 100, 2),
                    expected_volatility: round(minVarP.vol * 100, 2)
                }
            },
            efficient_frontier: frontier.map(p => ({
                expected_return: round(p.ret * 100, 2),
                volatility: round(p.vol * 100, 2),
                sharpe_ratio: round(p.sharpe, 2)
            })),
            diversification_metrics: {
                effective_number_of_assets: round(effectiveN, 2),
                concentration_index: round(herfindahl * 100, 2),
                max_weight: round(maxW * 100, 2),
                top_5_concentration: round(top5 * 100, 2),
                diversification_score: round((effectiveN / sortedW.length) * 100, 1)
            },
            constraints_applied: constraints
        };
    })
    .build();

/** Scale weight changes down to the turnover cap, then renormalize (C# parity). */
function applyTurnoverConstraint(
    newWeights: Record<string, number>,
    currentWeights: Record<string, number>,
    maxTurnover: number): Record<string, number> {
    let turnover = 0;
    for (const k of Object.keys(newWeights)) {
        turnover += Math.abs(newWeights[k] - (currentWeights[k] ?? 0));
    }
    if (turnover <= maxTurnover) return newWeights;

    const scale = maxTurnover / turnover;
    const adjusted: Record<string, number> = {};
    let total = 0;
    for (const k of Object.keys(newWeights)) {
        const current = currentWeights[k] ?? 0;
        adjusted[k] = current + (newWeights[k] - current) * scale;
        total += adjusted[k];
    }
    if (total > 0) for (const k of Object.keys(adjusted)) adjusted[k] /= total;
    return adjusted;
}

// -------------------------------------------------------- black_litterman

export const blackLitterman = toolBuilder()
    .name("black_litterman")
    .description("Performs Black-Litterman portfolio optimization combining market equilibrium with investor views. Blends market-implied expected returns with subjective forecasts using Bayesian approach. Produces stable, diversified portfolios with explicit confidence levels for views.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbols: { type: "array", description: "Array of symbols (e.g., ['AAPL', 'MSFT', 'GOOGL'])" },
                returns_data: { type: "object", description: "Dictionary of symbol -> historical returns array" },
                market_caps: { type: "object", description: "Dictionary of symbol -> market capitalization (for equilibrium weights)" },
                investor_views: { type: "array", description: "Array of investor views: [{type: 'absolute/relative', assets: ['AAPL'], return: 0.15, confidence: 0.7}]" },
                risk_aversion: { type: "number", description: "Market risk aversion parameter", default: 2.5 },
                tau: { type: "number", description: "Uncertainty scaling parameter", default: 0.05 },
                risk_free_rate: { type: "number", description: "Risk-free rate", default: 0.02 }
            },
            required: ["symbols", "returns_data", "market_caps"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                method: { type: "string" },
                description: { type: "string" },
                symbols: { type: "array" },
                optimal_weights: { type: "object" },
                expected_return: { type: "number" },
                expected_volatility: { type: "number" },
                sharpe_ratio: { type: "number" },
                implied_returns: { type: "object" },
                posterior_returns: { type: "object" },
                return_adjustments: { type: "object" },
                investor_views: { type: "array" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const symbols = dedupe(input.symbols);
        if (symbols.length < 2) throw new Error("At least 2 symbols required for optimization");

        const returnsData = input.returns_data ?? {};
        const marketCaps = input.market_caps ?? {};
        const views: any[] = Array.isArray(input.investor_views) ? input.investor_views : [];
        const riskAversion = num(input.risk_aversion, 2.5);
        const tau = num(input.tau, 0.05);
        const riskFreeRate = num(input.risk_free_rate, 0.02);

        // Market equilibrium weights from market caps, normalized over the
        // REQUESTED symbols (the C# divided by the whole dictionary's sum,
        // which broke sum-to-1 when the dictionary held extra symbols).
        let totalCap = 0;
        for (const s of symbols) totalCap += num(marketCaps[s], 0);
        const marketWeights: Record<string, number> = {};
        for (const s of symbols) {
            marketWeights[s] = totalCap > 0 ? num(marketCaps[s], 0) / totalCap : 1 / symbols.length;
        }

        const cov = covarianceMatrix(symbols, returnsData);

        // Implied equilibrium returns: Pi = delta * Sigma * w_mkt (verbatim port).
        const implied: Record<string, number> = {};
        for (const s of symbols) {
            let acc = 0;
            for (const s2 of symbols) acc += (cov[s]?.[s2] ?? 0) * marketWeights[s2];
            implied[s] = riskAversion * acc;
        }

        // SIMPLIFICATION: no [(tS)^-1 + P'O^-1 P]^-1 posterior — the C# itself
        // replaced it with an unbounded "confidence * tau" multiplier that
        // exploded returns. This port uses an honest convex blend instead:
        // absolute view -> posterior = (1-c)*prior + c*view_return; relative
        // view -> +/- c*spread/2 on the two legs. tau rescales view strength
        // relative to its 0.05 default (tau = 0.05 leaves confidence as-is),
        // clamped so the blend stays convex.
        const posterior: Record<string, number> = { ...implied };
        const hasViews = views.length > 0;
        for (const v of views) {
            const type = String(v?.type ?? "");
            const assets: string[] = Array.isArray(v?.assets) ? v.assets.map(String) : [];
            const viewReturn = num(v?.return, 0);
            const confidence = Math.min(1, Math.max(0, num(v?.confidence, 0.5)));
            const strength = Math.min(1, confidence * (tau / 0.05));
            if (type === "absolute") {
                for (const a of assets) {
                    if (posterior[a] !== undefined) {
                        posterior[a] = (1 - strength) * posterior[a] + strength * viewReturn;
                    }
                }
            } else if (type === "relative" && assets.length >= 2) {
                const [long, short] = assets;
                if (posterior[long] !== undefined) posterior[long] += strength * viewReturn / 2;
                if (posterior[short] !== undefined) posterior[short] -= strength * viewReturn / 2;
            }
        }

        // Optimal weights: w_i proportional to max(0, E[R_i] / (delta * sigma_i^2))
        // — the same per-asset proportional heuristic the C# used instead of
        // inverting (delta * Sigma). Falls back to market weights when every
        // posterior return is non-positive (the C# silently returned all-zero
        // weights there, breaking the sum-to-1 invariant).
        let weights: Record<string, number>;
        if (!hasViews) {
            weights = { ...marketWeights };
        } else {
            weights = {};
            let total = 0;
            for (const s of symbols) {
                const variance = Math.max(1e-10, cov[s]?.[s] ?? 0.01);
                const w = Math.max(0, posterior[s] / (riskAversion * variance));
                weights[s] = w;
                total += w;
            }
            if (total > 0) {
                for (const s of symbols) weights[s] /= total;
            } else {
                weights = { ...marketWeights };
            }
        }

        const pRet = portfolioReturn(weights, posterior);
        const pVol = portfolioVolatility(weights, cov);
        const sharpe = pVol > 0 ? (pRet - riskFreeRate) / pVol : 0;

        const scale100 = (m: Record<string, number>): Record<string, number> => {
            const out: Record<string, number> = {};
            for (const k of Object.keys(m)) out[k] = round(m[k] * 100, 2);
            return out;
        };
        const adjustments: Record<string, number> = {};
        for (const s of symbols) adjustments[s] = round((posterior[s] - implied[s]) * 100, 2);

        return {
            timestamp: isoNow(),
            method: "BLACK_LITTERMAN",
            description: hasViews
                ? "Black-Litterman portfolio with investor views"
                : "Market equilibrium portfolio (no views)",
            symbols,
            optimal_weights: roundWeightsSummingToOne(weights),
            expected_return: round(pRet * 100, 2),
            expected_volatility: round(pVol * 100, 2),
            sharpe_ratio: round(sharpe, 2),
            implied_returns: scale100(implied),
            posterior_returns: scale100(posterior),
            return_adjustments: adjustments,
            investor_views: hasViews
                ? views.map((v: any) => ({
                    type: String(v?.type ?? ""),
                    assets: Array.isArray(v?.assets) ? v.assets : [],
                    expected_return: round(num(v?.return, 0) * 100, 2),
                    confidence: round(num(v?.confidence, 0) * 100, 0)
                }))
                : null
        };
    })
    .build();

// ------------------------------------------------ hierarchical_risk_parity

interface ClusterNode {
    symbols: string[];
    isLeaf: boolean;
    left: ClusterNode | null;
    right: ClusterNode | null;
    distance: number;
}

export const hierarchicalRiskParity = toolBuilder()
    .name("hierarchical_risk_parity")
    .description("Constructs Hierarchical Risk Parity (HRP) portfolio using machine learning clustering. Groups similar assets via hierarchical clustering, applies risk parity allocation. More stable than Mean-Variance, handles fat tails and regime changes robustly.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbols: { type: "array", description: "Array of symbols (e.g., ['AAPL', 'MSFT', 'GOOGL', 'SPY', 'TLT'])" },
                returns_data: { type: "object", description: "Dictionary of symbol -> historical returns array" },
                linkage_method: { type: "string", description: "Clustering linkage method: 'single', 'complete', 'average', 'ward'", default: "ward" },
                risk_free_rate: { type: "number", description: "Risk-free rate", default: 0.02 }
            },
            required: ["symbols", "returns_data"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                method: { type: "string" },
                symbols: { type: "array" },
                optimal_weights: { type: "object" },
                expected_return: { type: "number" },
                expected_volatility: { type: "number" },
                sharpe_ratio: { type: "number" },
                cluster_structure: { type: "object" },
                sorted_order: { type: "array" },
                linkage_method: { type: "string" },
                diversification_ratio: { type: "number" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const symbols = dedupe(input.symbols);
        if (symbols.length < 2) {
            throw new Error("Hierarchical Risk Parity requires at least 2 assets for clustering. Cannot create hierarchy with single asset.");
        }
        const returnsData = input.returns_data ?? {};
        const linkageMethod = typeof input.linkage_method === "string" && input.linkage_method !== ""
            ? input.linkage_method : "ward";
        const riskFreeRate = num(input.risk_free_rate, 0.02);

        const corr = correlationMatrix(symbols, returnsData);
        const cov = covarianceMatrix(symbols, returnsData);

        // Correlation distance: d = sqrt(0.5 * (1 - rho)) — Lopez de Prado.
        const dist: Matrix = {};
        for (const s1 of symbols) {
            dist[s1] = {};
            for (const s2 of symbols) dist[s1][s2] = Math.sqrt(0.5 * (1 - (corr[s1]?.[s2] ?? 0)));
        }

        // SIMPLIFICATION (inherited from the C#): agglomerative clustering with
        // pairwise-distance linkage where "ward" degrades to "average"; fine at
        // the handful-of-assets scale these mock portfolios run at.
        const dendrogram = cluster(symbols, dist, linkageMethod);

        // Quasi-diagonalization: in-order dendrogram traversal.
        const sortedOrder: string[] = [];
        (function traverse(node: ClusterNode | null) {
            if (node === null) return;
            if (node.isLeaf) { sortedOrder.push(...node.symbols); return; }
            traverse(node.left);
            traverse(node.right);
        })(dendrogram);

        // SIMPLIFICATION (inherited): recursive bisection splits the sorted
        // list at the midpoint (not at the dendrogram cut) and allocates each
        // half inversely to its equal-weighted cluster variance. Variances are
        // floored at 1e-10 — the C# divided by a raw variance and produced
        // NaN weights on degenerate (constant-return) mock series.
        const weights: Record<string, number> = {};
        (function bisect(syms: string[], totalWeight: number) {
            if (syms.length === 1) { weights[syms[0]] = totalWeight; return; }
            const mid = Math.floor(syms.length / 2);
            const left = syms.slice(0, mid);
            const right = syms.slice(mid);
            const leftVar = Math.max(1e-10, clusterVariance(left, cov));
            const rightVar = Math.max(1e-10, clusterVariance(right, cov));
            const totalInvVar = 1 / leftVar + 1 / rightVar;
            bisect(left, (1 / leftVar) / totalInvVar * totalWeight);
            bisect(right, (1 / rightVar) / totalInvVar * totalWeight);
        })(sortedOrder, 1.0);

        const expected = expectedReturnsOf(symbols, returnsData);
        const pRet = portfolioReturn(weights, expected);
        const pVol = portfolioVolatility(weights, cov);
        const sharpe = pVol > 0 ? (pRet - riskFreeRate) / pVol : 0;

        // Cluster structure report: every internal node, ordered by depth.
        const clusters: { depth: number; symbols: string[]; size: number; total_weight: number; distance: number }[] = [];
        (function analyze(node: ClusterNode | null, depth: number) {
            if (node === null || node.isLeaf) return;
            let clusterWeight = 0;
            for (const s of node.symbols) clusterWeight += weights[s] ?? 0;
            clusters.push({
                depth,
                symbols: node.symbols,
                size: node.symbols.length,
                total_weight: round(clusterWeight * 100, 2),
                distance: round(node.distance, 4)
            });
            analyze(node.left, depth + 1);
            analyze(node.right, depth + 1);
        })(dendrogram, 0);
        clusters.sort((a, b) => a.depth - b.depth);
        let maxDepth = 0;
        for (const c of clusters) if (c.depth > maxDepth) maxDepth = c.depth;

        return {
            timestamp: isoNow(),
            method: "HIERARCHICAL_RISK_PARITY",
            symbols,
            optimal_weights: roundWeightsSummingToOne(weights),
            expected_return: round(pRet * 100, 2),
            expected_volatility: round(pVol * 100, 2),
            sharpe_ratio: round(sharpe, 2),
            cluster_structure: {
                num_clusters: clusters.length,
                clusters,
                max_depth: maxDepth
            },
            sorted_order: sortedOrder,
            linkage_method: linkageMethod,
            diversification_ratio: diversificationRatio(weights, cov)
        };
    })
    .build();

function cluster(symbols: readonly string[], dist: Matrix, linkageMethod: string): ClusterNode {
    let clusters: ClusterNode[] = symbols.map(s => ({
        symbols: [s], isLeaf: true, left: null, right: null, distance: 0
    }));
    while (clusters.length > 1) {
        let minDistance = Infinity;
        let minI = 0, minJ = 1;
        for (let i = 0; i < clusters.length; i++) {
            for (let j = i + 1; j < clusters.length; j++) {
                const d = linkageDistance(clusters[i].symbols, clusters[j].symbols, dist, linkageMethod);
                if (d < minDistance) { minDistance = d; minI = i; minJ = j; }
            }
        }
        const merged: ClusterNode = {
            symbols: [...clusters[minI].symbols, ...clusters[minJ].symbols],
            isLeaf: false,
            left: clusters[minI],
            right: clusters[minJ],
            distance: minDistance
        };
        clusters = clusters.filter((_, idx) => idx !== minI && idx !== minJ);
        clusters.push(merged);
    }
    return clusters[0];
}

function linkageDistance(
    cluster1: readonly string[],
    cluster2: readonly string[],
    dist: Matrix,
    linkageMethod: string): number {
    const distances: number[] = [];
    for (const s1 of cluster1) {
        for (const s2 of cluster2) distances.push(dist[s1]?.[s2] ?? 1.0);
    }
    switch (linkageMethod.toLowerCase()) {
        case "single": return Math.min(...distances);
        case "complete": return Math.max(...distances);
        default: return mean(distances); // "average" and the simplified "ward"
    }
}

function clusterVariance(symbols: readonly string[], cov: Matrix): number {
    if (symbols.length === 1) return cov[symbols[0]]?.[symbols[0]] ?? 0.01;
    const equal: Record<string, number> = {};
    for (const s of symbols) equal[s] = 1 / symbols.length;
    const vol = portfolioVolatility(equal, cov);
    return vol * vol;
}

// ------------------------------------------------------------- risk_parity

export const riskParity = toolBuilder()
    .name("risk_parity")
    .description("Constructs Risk Parity portfolio where each asset contributes equally to portfolio risk. Allocates more to low-volatility assets, less to high-volatility assets. Produces well-diversified portfolios suitable for all-weather strategies.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                symbols: { type: "array", description: "Array of symbols (e.g., ['SPY', 'TLT', 'GLD', 'DBC'])" },
                returns_data: { type: "object", description: "Dictionary of symbol -> historical returns array" },
                target_risk_contributions: { type: "object", description: "Optional: Dictionary of symbol -> target risk contribution (must sum to 1.0)" },
                risk_free_rate: { type: "number", description: "Risk-free rate", default: 0.02 }
            },
            required: ["symbols", "returns_data"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                method: { type: "string" },
                symbols: { type: "array" },
                optimal_weights: { type: "object" },
                risk_contributions: { type: "object" },
                target_risk_contributions: { type: "object" },
                marginal_risk_contributions: { type: "object" },
                expected_return: { type: "number" },
                expected_volatility: { type: "number" },
                sharpe_ratio: { type: "number" },
                diversification_ratio: { type: "number" },
                risk_balance_score: { type: "number" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const symbols = dedupe(input.symbols);
        if (symbols.length < 2) {
            throw new Error("Risk Parity requires at least 2 assets for diversification. Cannot optimize with single asset.");
        }
        const returnsData = input.returns_data ?? {};
        const riskFreeRate = num(input.risk_free_rate, 0.02);

        const cov = covarianceMatrix(symbols, returnsData);
        const expected = expectedReturnsOf(symbols, returnsData);

        const targets: Record<string, number> = {};
        const providedTargets = input.target_risk_contributions;
        for (const s of symbols) {
            targets[s] = providedTargets && typeof providedTargets[s] === "number"
                ? providedTargets[s]
                : 1 / symbols.length;
        }

        // SIMPLIFICATION (inherited from the C#): no Newton / cyclical
        // coordinate-descent solver — a multiplicative iteration nudges each
        // weight by 1 + 0.1 * (target contribution - actual contribution),
        // clamps at zero, renormalizes, and stops at 1e-6 max error or 1000
        // rounds. Converges fast on the small mock covariance matrices.
        let weights: Record<string, number> = {};
        for (const s of symbols) weights[s] = 1 / symbols.length;
        for (let iter = 0; iter < 1000; iter++) {
            const rc = riskContributions(weights, cov);
            let maxError = 0;
            for (const s of symbols) {
                const e = Math.abs(rc[s] - targets[s]);
                if (e > maxError) maxError = e;
            }
            if (maxError < 1e-6) break;
            const next: Record<string, number> = {};
            let total = 0;
            for (const s of symbols) {
                const w = Math.max(0, weights[s] * (1 + 0.1 * (targets[s] - rc[s])));
                next[s] = w;
                total += w;
            }
            if (total <= 0) break; // degenerate covariance: keep last feasible weights
            for (const s of symbols) weights[s] = next[s] / total;
        }

        const actualRc = riskContributions(weights, cov);
        const pRet = portfolioReturn(weights, expected);
        const pVol = portfolioVolatility(weights, cov);
        const sharpe = pVol > 0 ? (pRet - riskFreeRate) / pVol : 0;

        // Marginal risk contributions: (Sigma * w)_i / sigma_p.
        const marginal: Record<string, number> = {};
        for (const s of symbols) {
            let acc = 0;
            for (const s2 of symbols) acc += (cov[s]?.[s2] ?? 0) * weights[s2];
            marginal[s] = pVol > 0 ? acc / pVol : 0;
        }

        // Risk balance score: 100 - mean absolute deviation from targets * 100.
        let devSum = 0;
        for (const s of symbols) devSum += Math.abs(actualRc[s] - targets[s]);
        const score = Math.max(0, 100 - (devSum / symbols.length) * 100);

        const pct2 = (m: Record<string, number>): Record<string, number> => {
            const out: Record<string, number> = {};
            for (const k of Object.keys(m)) out[k] = round(m[k] * 100, 2);
            return out;
        };
        const marginalPct: Record<string, number> = {};
        for (const k of Object.keys(marginal)) marginalPct[k] = round(marginal[k] * 100, 4);

        return {
            timestamp: isoNow(),
            method: "RISK_PARITY",
            symbols,
            optimal_weights: roundWeightsSummingToOne(weights),
            risk_contributions: pct2(actualRc),
            target_risk_contributions: pct2(targets),
            marginal_risk_contributions: marginalPct,
            expected_return: round(pRet * 100, 2),
            expected_volatility: round(pVol * 100, 2),
            sharpe_ratio: round(sharpe, 2),
            diversification_ratio: diversificationRatio(weights, cov),
            risk_balance_score: round(score, 1)
        };
    })
    .build();

/** Risk contribution per asset: w_i * (Sigma w)_i / (w' Sigma w); equal split on zero variance. */
function riskContributions(weights: Record<string, number>, cov: Matrix): Record<string, number> {
    const keys = Object.keys(weights);
    const marginal: Record<string, number> = {};
    for (const s of keys) {
        let acc = 0;
        for (const s2 of keys) acc += (cov[s]?.[s2] ?? 0) * weights[s2];
        marginal[s] = acc;
    }
    let portfolioVariance = 0;
    for (const s of keys) portfolioVariance += weights[s] * marginal[s];
    const out: Record<string, number> = {};
    for (const s of keys) {
        out[s] = portfolioVariance > 0
            ? weights[s] * marginal[s] / portfolioVariance
            : 1 / keys.length;
    }
    return out;
}

// --------------------------------------------------- portfolio_rebalancing

export const portfolioRebalancing = toolBuilder()
    .name("portfolio_rebalancing")
    .description("Generates optimal rebalancing trade list from current to target portfolio weights. Minimizes transaction costs, supports threshold-based and calendar-based rebalancing. Includes tax-loss harvesting opportunities and trade execution recommendations.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                current_portfolio: { type: "object", description: "Current portfolio: {symbol: {weight, quantity, price, cost_basis}}" },
                target_weights: { type: "object", description: "Target portfolio weights (must sum to 1.0)" },
                portfolio_value: { type: "number", description: "Total portfolio value" },
                rebalancing_threshold: { type: "number", description: "Minimum weight deviation to trigger rebalancing (default: 0.05 = 5%)", default: 0.05 },
                commission_per_trade: { type: "number", description: "Commission per trade in USD", default: 0.0 },
                tax_rate: { type: "number", description: "Capital gains tax rate for tax-loss harvesting (default: 0.2 = 20%)", default: 0.2 },
                consider_tax_loss_harvesting: { type: "boolean", description: "Consider tax-loss harvesting opportunities", default: false },
                max_trades: { type: "integer", description: "Maximum number of trades to execute (optional)" }
            },
            required: ["current_portfolio", "target_weights", "portfolio_value"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                portfolio_value: { type: "number" },
                rebalancing_threshold: { type: "number" },
                trades_count: { type: "integer" },
                trades: { type: "array" },
                total_turnover: { type: "number" },
                estimated_costs: { type: "object" },
                execution_plan: { type: "object" },
                current_weights: { type: "object" },
                target_weights: { type: "object" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const currentPortfolio = input.current_portfolio ?? {};
        const targetWeights: Record<string, number> = input.target_weights ?? {};
        const portfolioValue = num(input.portfolio_value, 0);
        const threshold = num(input.rebalancing_threshold, 0.05);
        const commissionPerTrade = num(input.commission_per_trade, 0.0);
        const considerTaxLoss = input.consider_tax_loss_harvesting === true;
        const maxTrades = typeof input.max_trades === "number" ? input.max_trades : null;

        // Positions carry {weight, quantity, current_price, cost_basis} in the
        // C# contract; the YAML description said "price", so both spellings
        // are accepted here.
        const positionOf = (symbol: string) => {
            const p = currentPortfolio[symbol] ?? {};
            return {
                quantity: num(p.quantity, 0),
                price: num(p.current_price, num(p.price, 0)),
                costBasis: num(p.cost_basis, 0)
            };
        };

        // Current weights from position values.
        const currentWeights: Record<string, number> = {};
        for (const symbol of Object.keys(currentPortfolio)) {
            const pos = positionOf(symbol);
            currentWeights[symbol] = portfolioValue > 0 ? pos.quantity * pos.price / portfolioValue : 0;
        }

        // Union of current and target symbols; trade when drift beats threshold.
        const allSymbols: string[] = [];
        const seen: Record<string, boolean> = {};
        for (const s of Object.keys(currentWeights)) { if (!seen[s]) { seen[s] = true; allSymbols.push(s); } }
        for (const s of Object.keys(targetWeights)) { if (!seen[s]) { seen[s] = true; allSymbols.push(s); } }

        interface Trade {
            symbol: string; action: string;
            currentWeight: number; targetWeight: number;
            currentQuantity: number; targetQuantity: number;
            estimatedValue: number; currentPrice: number; reason: string;
        }
        const candidates: Trade[] = [];
        for (const symbol of allSymbols) {
            const currentWeight = currentWeights[symbol] ?? 0;
            const targetWeight = num(targetWeights[symbol], 0);
            const diff = targetWeight - currentWeight;
            if (Math.abs(diff) < threshold) continue;

            const pos = positionOf(symbol);
            const targetValue = portfolioValue * targetWeight;
            const targetQuantity = pos.price > 0 ? targetValue / pos.price : 0;
            const absDiff = Math.abs(diff);
            const reason = currentWeight === 0 ? "New position"
                : targetWeight === 0 ? "Liquidate position"
                : absDiff > threshold * 3 ? "Major allocation adjustment"
                : absDiff > threshold * 2 ? "Significant drift from target"
                : "Routine rebalancing";

            candidates.push({
                symbol,
                action: diff > 0 ? "BUY" : "SELL",
                currentWeight,
                targetWeight,
                currentQuantity: pos.quantity,
                targetQuantity,
                estimatedValue: Math.abs(targetValue - pos.quantity * pos.price),
                currentPrice: pos.price,
                reason
            });
        }

        // Prioritize cost-effective trades: large value first, commission-heavy last.
        const scoreOf = (t: Trade): number => {
            const valueScore = t.estimatedValue / 1000;
            const costEffectiveness = t.estimatedValue > 0
                ? 1 - Math.min(1, commissionPerTrade / t.estimatedValue)
                : 0;
            return valueScore * costEffectiveness;
        };
        let trades = candidates.slice().sort((a, b) => scoreOf(b) - scoreOf(a));
        if (maxTrades !== null && trades.length > maxTrades) trades = trades.slice(0, maxTrades);

        let totalTurnover = 0;
        for (const t of trades) totalTurnover += Math.abs(t.targetWeight - t.currentWeight);

        const totalCommission = trades.length * commissionPerTrade;
        let estimatedSlippage = 0;
        for (const t of trades) estimatedSlippage += t.estimatedValue * 0.0005; // 0.05% slippage
        const estimatedCosts: Record<string, unknown> = {
            commission: round(totalCommission, 2),
            estimated_slippage: round(estimatedSlippage, 2),
            total_transaction_cost: round(totalCommission + estimatedSlippage, 2)
        };
        // The C# computed a tax-loss opportunity list but only ever surfaced
        // this note in the response — same shape here.
        if (considerTaxLoss) estimatedCosts.tax_considerations = "Tax-loss harvesting opportunities analyzed";

        let sellCount = 0, buyCount = 0, hasLargeOrder = false;
        for (const t of trades) {
            if (t.action === "SELL") sellCount++;
            if (t.action === "BUY") buyCount++;
            if (t.estimatedValue > 100000) hasLargeOrder = true;
        }

        return {
            timestamp: isoNow(),
            portfolio_value: portfolioValue,
            rebalancing_threshold: threshold,
            trades_count: trades.length,
            trades: trades.map(t => ({
                symbol: t.symbol,
                action: t.action,
                current_weight: round(t.currentWeight * 100, 2),
                target_weight: round(t.targetWeight * 100, 2),
                weight_change: round((t.targetWeight - t.currentWeight) * 100, 2),
                current_quantity: t.currentQuantity,
                target_quantity: round(t.targetQuantity, 0),
                quantity_change: round(t.targetQuantity - t.currentQuantity, 0),
                estimated_value: round(t.estimatedValue, 2),
                current_price: t.currentPrice,
                priority: "NORMAL",
                reason: t.reason
            })),
            total_turnover: round(totalTurnover * 100, 2),
            estimated_costs: estimatedCosts,
            execution_plan: {
                recommended_sequence: [
                    "1. Execute SELL orders first to free up capital",
                    "2. Wait for settlement (T+2 for equities)",
                    "3. Execute BUY orders with proceeds",
                    "4. Monitor execution and adjust for slippage"
                ],
                sell_orders_count: sellCount,
                buy_orders_count: buyCount,
                estimated_duration: "2-3 trading days",
                execution_algorithm_recommendation: hasLargeOrder
                    ? "Use VWAP/TWAP for large orders (>$100k)"
                    : "Market orders acceptable for small trades"
            },
            current_weights: scaleWeightsToPct(currentWeights),
            target_weights: (() => {
                const out: Record<string, number> = {};
                for (const k of Object.keys(targetWeights)) out[k] = round(num(targetWeights[k], 0) * 100, 2);
                return out;
            })()
        };
    })
    .build();
