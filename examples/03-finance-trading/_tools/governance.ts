// Governance tools (EX-01) — the TypeScript port of Orkeon.Trading.Tools'
// Infrastructure/Governance. Same names, same schemas (translated verbatim from
// the YAML ToolDefinitions), same output shapes; identifiers are DETERMINISTIC
// (derived from the input) where the C# used Guid.NewGuid. All five are pure
// mock computation — no disk, no network — hence access("read").

import { seedFrom } from "./math.ts";

function deterministicId(prefix: string, input: unknown): string {
    return `${prefix}-${seedFrom(JSON.stringify(input)).toString(16).padStart(8, "0")}`;
}

function isoNow(): string {
    return new Date().toISOString();
}

function isoDatePlusDays(days: number): string {
    const d = new Date(Date.now() + days * 86400000);
    return d.toISOString().slice(0, 10);
}

function num(v: unknown, fallback: number): number {
    return typeof v === "number" && !Number.isNaN(v) ? v : fallback;
}

export const auditTrail = toolBuilder()
    .name("audit_trail")
    .description("Creates immutable audit log entries for all trading activities.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                action: { type: "string", description: "Action performed" },
                actor: { type: "string", description: "Who performed action" },
                details: { type: "object", description: "Action details" }
            },
            required: ["action", "actor", "details"]
        },
        output: {
            type: "object",
            properties: {
                audit_entry_created: { type: "boolean" },
                audit_id: { type: "string" },
                timestamp: { type: "string" }
            }
        }
    })
    .access("read")
    .execute((input: any) => ({
        audit_entry_created: true,
        audit_id: deterministicId("audit", input),
        timestamp: isoNow()
    }))
    .build();

export const alertManagement = toolBuilder()
    .name("alert_management")
    .description("Manages trading alerts and notifications for critical events.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                alert_type: { type: "string", description: "Alert type: 'RISK', 'COMPLIANCE', 'SYSTEM', 'MARKET'" },
                severity: { type: "string", description: "Severity: 'INFO', 'WARNING', 'CRITICAL'" },
                message: { type: "string", description: "Alert message" }
            },
            required: ["alert_type", "severity", "message"]
        },
        output: {
            type: "object",
            properties: {
                alert_created: { type: "boolean" },
                alert_id: { type: "string" },
                notification_sent: { type: "boolean" },
                channels: { type: "array" }
            }
        }
    })
    .access("read")
    .execute((input: any) => ({
        alert_created: true,
        alert_id: deterministicId("alert", input),
        notification_sent: true,
        channels: input.severity === "CRITICAL" ? ["email", "sms", "slack"] : ["email"]
    }))
    .build();

export const complianceCheck = toolBuilder()
    .name("compliance_check")
    .description("Validates trades against regulatory and internal compliance rules.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                trade: { type: "object", description: "Proposed trade" },
                rules: { type: "array", description: "Compliance rules" }
            },
            required: ["trade", "rules"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                trade_approved: { type: "boolean" },
                rules_checked: { type: "integer" },
                violations: { type: "array" },
                compliance_checks: { type: "array" },
                action: { type: "string" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        // Same simplification as the C# original: every known rule passes; the
        // value of the tool is the structured verdict, not a real rule engine.
        const known = ["position_limit", "wash_sale", "restricted_list", "concentration"];
        const rules: string[] = Array.isArray(input.rules) ? input.rules : [];
        const checks = rules.map(rule => {
            const passed = known.includes(rule) || true;
            return { rule, passed, status: passed ? "PASS" : "FAIL" };
        });
        const violations = checks.filter(c => !c.passed).map(c => c.rule);
        return {
            timestamp: isoNow(),
            trade_approved: violations.length === 0,
            rules_checked: checks.length,
            violations,
            compliance_checks: checks,
            action: violations.length > 0 ? "REJECT_TRADE" : "APPROVE_TRADE"
        };
    })
    .build();

export const dashboardMetrics = toolBuilder()
    .name("dashboard_metrics")
    .description("Aggregates real-time metrics for trading dashboard and monitoring.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                portfolio_data: { type: "object", description: "Current portfolio state" },
                market_data: { type: "object", description: "Current market data" }
            },
            required: ["portfolio_data"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                portfolio: { type: "object" },
                risk: { type: "object" },
                trading: { type: "object" },
                alerts: { type: "object" },
                market: { type: "object" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const p = input.portfolio_data ?? {};
        const m = input.market_data ?? {};
        const hasMarket = Object.keys(m).length > 0;
        return {
            timestamp: isoNow(),
            portfolio: {
                total_value: num(p.total_value, 0),
                daily_pnl: num(p.daily_pnl, 0),
                daily_return_pct: num(p.daily_return_pct, 0),
                positions_count: num(p.positions_count, 0),
                cash_balance: num(p.cash, 0)
            },
            risk: {
                var_95: num(p.var_95, 0),
                sharpe_ratio: num(p.sharpe_ratio, 0),
                max_drawdown_pct: num(p.max_drawdown, 0),
                leverage: num(p.leverage, 1.0)
            },
            trading: {
                orders_today: num(p.orders_today, 0),
                fill_rate_pct: num(p.fill_rate, 95.0),
                avg_execution_time_ms: num(p.avg_execution_ms, 50)
            },
            alerts: { active_alerts: 0, critical_alerts: 0 },
            market: hasMarket
                ? { spy_price: num(m.SPY, 0), vix: num(m.VIX, 0), market_status: "OPEN" }
                : null
        };
    })
    .build();

export const regulatoryReporting = toolBuilder()
    .name("regulatory_reporting")
    .description("Generates regulatory reports (Form PF, 13F, etc.) for compliance.")
    .withSchema({
        input: {
            type: "object",
            properties: {
                report_type: { type: "string", description: "Report type: '13F', 'FORM_PF', 'DAILY_VAR'" },
                portfolio_data: { type: "object", description: "Portfolio data for report" }
            },
            required: ["report_type", "portfolio_data"]
        },
        output: {
            type: "object",
            properties: {
                timestamp: { type: "string" },
                report_type: { type: "string" },
                report_generated: { type: "boolean" },
                report_data: { type: "object" },
                filing_deadline: { type: "string" }
            }
        }
    })
    .access("read")
    .execute((input: any) => {
        const data = input.portfolio_data ?? {};
        const type = String(input.report_type ?? "").toUpperCase();
        const today = isoDatePlusDays(0);
        let report: Record<string, unknown>;
        switch (type) {
            case "13F":
                report = {
                    form_type: "13F-HR",
                    total_value: num(data.total_value, 0),
                    holdings_count: num(data.positions_count, 0),
                    report_date: today
                };
                break;
            case "FORM_PF":
                report = {
                    form_type: "PF",
                    aum: num(data.aum, 0),
                    strategy: "Multi-Strategy",
                    leverage_ratio: num(data.leverage, 1.0)
                };
                break;
            case "DAILY_VAR":
                report = { var_95: num(data.var_95, 0), date: today };
                break;
            default:
                report = { error: "Unknown report type" };
                break;
        }
        return {
            timestamp: isoNow(),
            report_type: input.report_type,
            report_generated: true,
            report_data: report,
            filing_deadline: isoDatePlusDays(45)
        };
    })
    .build();
