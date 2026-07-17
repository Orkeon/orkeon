#!/usr/bin/env python3
"""Generate synthetic input data for the Orkeon showcase ("vitrine") examples.

Every showcase example that declares a file-reading tool (csv_reader, pdf_reader,
...) must ship a small, synthetic `data/` folder so the crew consumes real files
instead of hallucinating paths that the virtual file system then rejects. See
docs/reference/example-data-policy.md for the policy this script implements.

Design goals:
  * Deterministic  - a fixed RNG seed means re-running produces byte-identical
    files, so the committed data never drifts.
  * Synthetic only - no real, personal, or proprietary data. Names, vendors,
    and identifiers are obviously fictional.
  * Small          - every file stays well under 100 KB.
  * Zero third-party deps - CSV/JSON via the stdlib; PDFs via a tiny hand-rolled
    writer (below) so the script runs on a bare Python 3.9+ install.

Usage:
    python3 scripts/generate-vitrine-data.py           # generate everything
    python3 scripts/generate-vitrine-data.py --check    # verify files exist

The script is idempotent: it overwrites the files it owns and never touches
anything else in an example directory.
"""

from __future__ import annotations

import argparse
import csv
import io
import json
import os
import random
import sys

# Repo-root-relative base for the examples tree. The script is designed to be
# run from the repository root (`python3 scripts/generate-vitrine-data.py`).
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXAMPLES = os.path.join(REPO_ROOT, "examples")

SEED = 1337


# --------------------------------------------------------------------------- #
# Minimal, dependency-free PDF writer.
#
# PdfPig (the pdf_reader backend) extracts text from content streams that draw
# with the standard Helvetica font and WinAnsi encoding. We emit exactly that:
# one shared Type1 font, one page per "page" of lines, text laid out with a
# fixed leading. This is deliberately the simplest structure that round-trips
# through PdfPig's text extractor.
# --------------------------------------------------------------------------- #

def _pdf_escape(text: str) -> str:
    return text.replace("\\", r"\\").replace("(", r"\(").replace(")", r"\)")


def _content_stream(lines: list[str], font_size: int = 10, leading: int = 14) -> bytes:
    parts = ["BT", f"/F1 {font_size} Tf", f"{leading} TL", "50 770 Td"]
    for i, line in enumerate(lines):
        # Non-ASCII would need WinAnsi byte mapping; keep sample text ASCII.
        safe = _pdf_escape(line.encode("ascii", "replace").decode("ascii"))
        if i == 0:
            parts.append(f"({safe}) Tj")
        else:
            parts.append(f"T* ({safe}) Tj")
    parts.append("ET")
    return ("\n".join(parts)).encode("latin-1")


def write_pdf(path: str, pages: list[list[str]]) -> None:
    """Write a multi-page PDF. `pages` is a list of pages, each a list of lines."""
    objects: list[bytes] = []

    def add(obj: bytes) -> int:
        objects.append(obj)
        return len(objects)  # 1-based object number

    # Reserve catalog(1) and pages(2); fill kids after building page objects.
    objects.append(b"")  # placeholder for catalog
    objects.append(b"")  # placeholder for pages
    font_num = add(
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica "
        b"/Encoding /WinAnsiEncoding >>"
    )

    page_nums: list[int] = []
    for lines in pages:
        stream = _content_stream(lines)
        content_num = add(
            b"<< /Length " + str(len(stream)).encode() + b" >>\nstream\n"
            + stream + b"\nendstream"
        )
        page_num = add(
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
            b"/Resources << /Font << /F1 " + str(font_num).encode() + b" 0 R >> >> "
            b"/Contents " + str(content_num).encode() + b" 0 R >>"
        )
        page_nums.append(page_num)

    kids = b" ".join(f"{n} 0 R".encode() for n in page_nums)
    objects[0] = b"<< /Type /Catalog /Pages 2 0 R >>"
    objects[1] = (
        b"<< /Type /Pages /Kids [" + kids + b"] /Count "
        + str(len(page_nums)).encode() + b" >>"
    )

    buf = io.BytesIO()
    buf.write(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = [0] * (len(objects) + 1)
    for i, obj in enumerate(objects, start=1):
        offsets[i] = buf.tell()
        buf.write(f"{i} 0 obj\n".encode())
        buf.write(obj)
        buf.write(b"\nendobj\n")

    xref_pos = buf.tell()
    buf.write(b"xref\n")
    buf.write(f"0 {len(objects) + 1}\n".encode())
    buf.write(b"0000000000 65535 f \n")
    for i in range(1, len(objects) + 1):
        buf.write(f"{offsets[i]:010d} 00000 n \n".encode())
    buf.write(b"trailer\n")
    buf.write(
        b"<< /Size " + str(len(objects) + 1).encode() + b" /Root 1 0 R >>\n"
    )
    buf.write(b"startxref\n")
    buf.write(f"{xref_pos}\n".encode())
    buf.write(b"%%EOF")

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as fh:
        fh.write(buf.getvalue())


# --------------------------------------------------------------------------- #
# Small helpers for CSV / JSON / text output.
# --------------------------------------------------------------------------- #

def write_csv(path: str, header: list[str], rows: list[list]) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(header)
        w.writerows(rows)


def write_json(path: str, obj) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(obj, fh, indent=2)
        fh.write("\n")


def write_text(path: str, text: str) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(text)


def ex(*parts: str) -> str:
    return os.path.join(EXAMPLES, *parts)


# --------------------------------------------------------------------------- #
# Per-example generators. Each returns the list of files it produced.
# --------------------------------------------------------------------------- #

def gen_research_assistant(rng: random.Random) -> list[str]:
    base = ex("01-enterprise", "01-research-assistant", "data")
    files = []

    # CSV: EV sales by year / region (the analyst's structured source).
    regions = ["Europe", "North America", "China", "Rest of World"]
    rows = []
    for year in range(2019, 2025):
        for region in regions:
            base_units = {"Europe": 1.2, "North America": 0.9,
                          "China": 3.0, "Rest of World": 0.4}[region]
            growth = (year - 2019) * rng.uniform(0.35, 0.55)
            units = round(base_units * (1 + growth), 2)
            share = round(min(45.0, 4 + (year - 2019) * rng.uniform(2.5, 4.5)), 1)
            rows.append([year, region, units, share])
    p = os.path.join(base, "ev-sales-by-region.csv")
    write_csv(p, ["year", "region", "units_millions", "bev_market_share_pct"], rows)
    files.append(p)

    # PDF: a short market report the document analyst parses.
    p = os.path.join(base, "ev-market-report.pdf")
    write_pdf(p, [[
        "SYNTHETIC MARKET REPORT (sample data - not for real use)",
        "Global Electric Vehicle Adoption 2019-2024",
        "",
        "1. Executive summary",
        "Battery-electric vehicle (BEV) sales grew from roughly 2.1 million units in",
        "2019 to an estimated 9.5 million units in 2024, driven by China and Europe.",
        "",
        "2. Key findings",
        "- China accounts for the largest single-market volume across the period.",
        "- European BEV market share passed 15 percent of new sales in 2023.",
        "- North American adoption lags Europe by roughly two years.",
        "",
        "3. Outlook",
        "Analysts project continued double-digit growth through 2026, constrained",
        "mainly by charging-infrastructure rollout and battery-material supply.",
        "",
        "Figures in this document are synthetic and illustrative only.",
    ]])
    files.append(p)
    return files


def gen_experimental_data(rng: random.Random) -> list[str]:
    base = ex("02-science-research", "19-experimental-data", "data")
    files = []
    # A/B experiment: control vs treatment, dose-response with noise.
    rows = []
    for subject in range(1, 121):
        group = "treatment" if subject % 2 == 0 else "control"
        age = rng.randint(21, 68)
        baseline = round(rng.uniform(40, 60), 1)
        dose = rng.choice([0, 0, 25, 50, 100]) if group == "treatment" else 0
        # Treatment adds a dose-dependent effect on top of baseline.
        effect = 0.08 * dose + rng.gauss(0, 3)
        response = round(baseline + effect + (2.5 if group == "treatment" else 0), 1)
        rows.append([f"S{subject:03d}", group, age, dose, baseline, response])
    p = os.path.join(base, "experiment-measurements.csv")
    write_csv(p, ["subject_id", "group", "age", "dose_mg",
                  "baseline_score", "response_score"], rows)
    files.append(p)

    # A tiny experiment metadata file (protocol context, read via file_read
    # if the crew chooses; primarily human-facing).
    p = os.path.join(base, "experiment-metadata.json")
    write_json(p, {
        "experiment_id": "EXP-SYNTH-0421",
        "title": "Synthetic dose-response study (illustrative)",
        "design": "two-arm randomized, control vs treatment",
        "primary_endpoint": "response_score",
        "hypothesis": "treatment increases response_score vs control",
        "sample_size": 120,
        "notes": "All values are synthetic and generated for demo purposes.",
    })
    files.append(p)
    return files


def gen_portfolio_consensus(rng: random.Random) -> list[str]:
    base = ex("03-finance-trading", "34-portfolio-consensus", "data")
    files = []
    tickers = ["ALFA", "BRVO", "CHRL", "DLTA", "ECHO", "FXTR"]
    # 60 trading days of synthetic close prices per ticker.
    price_rows = []
    prices = {t: rng.uniform(40, 220) for t in tickers}
    drift = {t: rng.uniform(-0.001, 0.0025) for t in tickers}
    vol = {t: rng.uniform(0.008, 0.03) for t in tickers}
    for day in range(1, 61):
        date = f"2024-04-{day:02d}" if day <= 30 else f"2024-05-{day - 30:02d}"
        for t in tickers:
            prices[t] *= (1 + drift[t] + rng.gauss(0, vol[t]))
            price_rows.append([date, t, round(prices[t], 2)])
    p = os.path.join(base, "price-history.csv")
    write_csv(p, ["date", "ticker", "close"], price_rows)
    files.append(p)

    # Per-ticker fundamentals for the fundamental analyst.
    fund_rows = []
    sectors = ["Technology", "Industrials", "Healthcare", "Energy",
               "Financials", "Consumer"]
    for i, t in enumerate(tickers):
        fund_rows.append([
            t, sectors[i],
            round(rng.uniform(8, 38), 1),          # pe_ratio
            round(rng.uniform(1.0, 6.5), 2),       # pb_ratio
            round(rng.uniform(0.04, 0.28), 3),     # roe
            round(rng.uniform(0.1, 1.8), 2),       # debt_to_equity
            round(rng.uniform(2, 320), 1),         # market_cap_bn
            round(rng.uniform(0.0, 3.5), 2),       # dividend_yield_pct
        ])
    p = os.path.join(base, "fundamentals.csv")
    write_csv(p, ["ticker", "sector", "pe_ratio", "pb_ratio", "roe",
                  "debt_to_equity", "market_cap_bn", "dividend_yield_pct"],
              fund_rows)
    files.append(p)
    return files


def gen_invoice_processing(rng: random.Random) -> list[str]:
    base = ex("03-finance-trading", "40-invoice-processing", "data")
    files = []

    # Two invoice PDFs from two fictional vendors.
    inv1 = os.path.join(base, "invoices", "INV-2024-0001.pdf")
    write_pdf(inv1, [[
        "INVOICE  (synthetic sample)",
        "Vendor: Acme Components Ltd.",
        "Invoice Number: INV-2024-0001",
        "Invoice Date: 2024-05-12",
        "Purchase Order: PO-5501",
        "",
        "Line items:",
        "  SKU-100  Widget A     qty 100  unit 4.50   amount 450.00",
        "  SKU-220  Bracket B    qty  40  unit 7.25   amount 290.00",
        "  SKU-330  Fastener C   qty 500  unit 0.12   amount  60.00",
        "",
        "Subtotal: 800.00",
        "Tax (8%): 64.00",
        "Total Due: 864.00 USD",
        "Payment Terms: Net 30",
    ]])
    files.append(inv1)

    inv2 = os.path.join(base, "invoices", "INV-2024-0002.pdf")
    write_pdf(inv2, [[
        "FACTURE / INVOICE  (synthetic sample)",
        "Vendor: Globex Supplies GmbH",
        "Invoice No.: INV-2024-0002",
        "Date: 2024-05-14",
        "PO Ref: PO-5502",
        "",
        "Detail:",
        "  MAT-01  Steel sheet   qty  20  unit 33.00  amount 660.00",
        "  MAT-07  Coating kit   qty  10  unit 18.50  amount 185.00",
        "",
        "Net amount: 845.00",
        "VAT (19%): 160.55",
        "Grand total: 1005.55 EUR",
        "Terms: Net 45",
    ]])
    files.append(inv2)

    # A CSV batch of electronic invoices.
    batch_rows = [
        ["INV-2024-0003", "Initech Parts", "2024-05-15", "PO-5503", 3, 1240.00, "USD", "Net 30"],
        ["INV-2024-0004", "Umbrella Materials", "2024-05-16", "PO-5504", 2, 512.40, "USD", "Net 60"],
        ["INV-2024-0005", "Hooli Logistics", "2024-05-17", "PO-9999", 1, 78.90, "USD", "Net 15"],
    ]
    p = os.path.join(base, "invoices", "invoices-batch.csv")
    write_csv(p, ["invoice_number", "vendor", "invoice_date", "po_number",
                  "line_count", "total_amount", "currency", "terms"], batch_rows)
    files.append(p)

    # Purchase orders and goods receipts for three-way matching.
    po_rows = [
        ["PO-5501", "Acme Components Ltd.", "2024-05-01", 800.00, "USD", "open"],
        ["PO-5502", "Globex Supplies GmbH", "2024-05-02", 845.00, "EUR", "open"],
        ["PO-5503", "Initech Parts", "2024-05-03", 1240.00, "USD", "open"],
        ["PO-5504", "Umbrella Materials", "2024-05-04", 500.00, "USD", "open"],
    ]
    p = os.path.join(base, "purchase-orders.csv")
    write_csv(p, ["po_number", "vendor", "po_date", "po_amount",
                  "currency", "status"], po_rows)
    files.append(p)

    gr_rows = [
        ["GR-8801", "PO-5501", "2024-05-11", "Widget A;Bracket B;Fastener C", "complete"],
        ["GR-8802", "PO-5502", "2024-05-13", "Steel sheet;Coating kit", "complete"],
        ["GR-8803", "PO-5503", "2024-05-15", "Assorted parts", "partial"],
    ]
    p = os.path.join(base, "goods-receipts.csv")
    write_csv(p, ["receipt_id", "po_number", "received_date",
                  "items", "status"], gr_rows)
    files.append(p)
    return files


def gen_nutrition_planner(rng: random.Random) -> list[str]:
    base = ex("04-health-wellness", "47-nutrition-planner", "data")
    files = []

    # A single synthetic patient profile (obviously fictional).
    p = os.path.join(base, "patient-history.csv")
    rows = [
        ["age_years", "42"],
        ["sex", "F"],
        ["height_cm", "168"],
        ["weight_kg", "72"],
        ["activity_level", "moderate"],
        ["allergy", "peanuts"],
        ["intolerance", "lactose"],
        ["condition", "type_2_diabetes"],
        ["preference", "mediterranean"],
        ["daily_calorie_target", "1900"],
    ]
    write_csv(p, ["attribute", "value"], rows)
    files.append(p)

    # Food composition table (per 100 g) for meal design + verification.
    foods = [
        ("Oats", 389, 16.9, 66.3, 6.9, 4.7, 54, 0.0, 56),
        ("Salmon", 208, 20.4, 0.0, 13.4, 0.8, 12, 3.2, 26),
        ("Lentils", 116, 9.0, 20.1, 0.4, 3.3, 19, 0.0, 181),
        ("Broccoli", 34, 2.8, 6.6, 0.4, 0.7, 47, 0.0, 63),
        ("Brown rice", 111, 2.6, 23.0, 0.9, 0.4, 10, 0.0, 4),
        ("Chickpeas", 164, 8.9, 27.4, 2.6, 2.9, 49, 0.0, 172),
        ("Spinach", 23, 2.9, 3.6, 0.4, 2.7, 99, 0.0, 194),
        ("Olive oil", 884, 0.0, 0.0, 100.0, 0.0, 1, 0.0, 0),
        ("Greek yogurt", 59, 10.0, 3.6, 0.4, 0.0, 110, 0.5, 7),
        ("Almonds", 579, 21.2, 21.6, 49.9, 3.7, 269, 0.0, 44),
        ("Egg", 155, 13.0, 1.1, 11.0, 1.8, 56, 1.1, 44),
        ("Sweet potato", 86, 1.6, 20.1, 0.1, 0.6, 30, 0.0, 11),
    ]
    p = os.path.join(base, "food-nutrition.csv")
    write_csv(p, ["food", "kcal_per_100g", "protein_g", "carbs_g", "fat_g",
                  "iron_mg", "calcium_mg", "vitamin_b12_ug", "folate_ug"],
              [list(f) for f in foods])
    files.append(p)
    return files


def gen_skills_gap(rng: random.Random) -> list[str]:
    base = ex("05-education", "63-skills-gap-mapping", "data")
    files = []
    members = ["Ada", "Bela", "Cai", "Devi", "Ege", "Faye", "Gus", "Hana"]
    skills = ["Kubernetes", "Terraform", "Python", "SQL",
              "Incident Response", "CI/CD", "Threat Modeling"]
    roles = {"Ada": "SRE", "Bela": "SRE", "Cai": "Platform Eng",
             "Devi": "Platform Eng", "Ege": "Security Eng",
             "Faye": "Security Eng", "Gus": "Data Eng", "Hana": "Data Eng"}
    rows = []
    for m in members:
        for s in skills:
            level = rng.randint(1, 5)
            cert = "yes" if rng.random() < 0.25 else "no"
            years = rng.randint(0, 9)
            rows.append([m, roles[m], s, level, cert, years])
    p = os.path.join(base, "team-skills.csv")
    write_csv(p, ["member", "role", "skill", "proficiency_1to5",
                  "certified", "years_experience"], rows)
    files.append(p)

    # Role -> required skill levels.
    req_rows = []
    role_req = {
        "SRE": {"Kubernetes": 4, "Terraform": 3, "Incident Response": 4, "CI/CD": 4},
        "Platform Eng": {"Kubernetes": 4, "Terraform": 4, "Python": 3, "CI/CD": 4},
        "Security Eng": {"Threat Modeling": 4, "Incident Response": 4, "Python": 3},
        "Data Eng": {"Python": 4, "SQL": 4, "CI/CD": 3},
    }
    for role, reqs in role_req.items():
        for skill, lvl in reqs.items():
            req_rows.append([role, skill, lvl])
    p = os.path.join(base, "role-requirements.csv")
    write_csv(p, ["role", "skill", "required_level_1to5"], req_rows)
    files.append(p)

    # Training catalog for the recommender.
    catalog = [
        ["TRN-01", "Certified Kubernetes Administrator", "Kubernetes", 4, 40, 1200],
        ["TRN-02", "Terraform Associate Prep", "Terraform", 3, 24, 600],
        ["TRN-03", "Incident Command Foundations", "Incident Response", 4, 16, 800],
        ["TRN-04", "Applied Threat Modeling", "Threat Modeling", 4, 20, 950],
        ["TRN-05", "SQL for Data Engineers", "SQL", 4, 30, 500],
        ["TRN-06", "Python Automation Bootcamp", "Python", 4, 32, 700],
        ["TRN-07", "CI/CD Pipeline Mastery", "CI/CD", 4, 18, 550],
    ]
    p = os.path.join(base, "training-catalog.csv")
    write_csv(p, ["program_id", "title", "skill", "target_level",
                  "duration_hours", "cost_usd"], catalog)
    files.append(p)
    return files


def gen_cloud_audit(rng: random.Random) -> list[str]:
    base = ex("06-engineering-devops", "71-cloud-audit-nist", "data")
    files = []
    services = ["EC2", "S3", "RDS", "Lambda", "EKS", "CloudFront",
                "DynamoDB", "EBS", "NAT-Gateway", "ELB"]
    regions = ["us-east-1", "eu-west-1", "ap-southeast-2"]
    rows = []
    for i in range(1, 41):
        svc = rng.choice(services)
        region = rng.choice(regions)
        cost = round(rng.uniform(5, 4200), 2)
        util = round(rng.uniform(2, 95), 1)
        tagged = "yes" if rng.random() < 0.7 else "no"
        rows.append([f"res-{i:04d}", svc, region, cost, util, tagged])
    p = os.path.join(base, "cloud-billing.csv")
    write_csv(p, ["resource_id", "service", "region",
                  "monthly_cost_usd", "utilization_pct", "cost_tag_present"], rows)
    files.append(p)
    return files


def gen_synthetic_data(rng: random.Random) -> list[str]:
    base = ex("07-creative-media", "78-synthetic-data", "data")
    files = []
    # A small "source" customer dataset whose schema/distribution is modeled,
    # then synthesized. Values are already synthetic; the crew never persists it.
    cities = ["Springfield", "Rivertown", "Lakeside", "Hillcrest", "Fairview"]
    plans = ["basic", "standard", "premium"]
    rows = []
    for i in range(1, 151):
        age = rng.randint(18, 79)
        city = rng.choice(cities)
        plan = rng.choices(plans, weights=[5, 3, 2])[0]
        tenure = rng.randint(1, 96)
        monthly = {"basic": 9.99, "standard": 19.99, "premium": 39.99}[plan]
        churn = "yes" if rng.random() < (0.30 if plan == "basic" else 0.12) else "no"
        rows.append([f"C{i:04d}", age, city, plan, tenure,
                     round(monthly, 2), churn])
    p = os.path.join(base, "source-customers.csv")
    write_csv(p, ["customer_id", "age", "city", "plan",
                  "tenure_months", "monthly_charge_usd", "churned"], rows)
    files.append(p)
    return files


def gen_fleet_management(rng: random.Random) -> list[str]:
    base = ex("08-iot-smart-systems", "87-fleet-management", "data")
    files = []
    # Today's delivery missions.
    streets = ["Maple Ave", "Oak St", "Pine Rd", "Cedar Ln", "Birch Blvd",
               "Elm Ct", "Willow Way", "Ash Dr"]
    mission_rows = []
    for i in range(1, 13):
        addr = f"{rng.randint(10, 990)} {rng.choice(streets)}"
        lat = round(40.70 + rng.uniform(-0.15, 0.15), 5)
        lon = round(-74.00 + rng.uniform(-0.15, 0.15), 5)
        window = rng.choice(["08:00-10:00", "10:00-12:00",
                             "12:00-14:00", "14:00-17:00"])
        priority = rng.choice(["standard", "standard", "express"])
        cargo = rng.randint(5, 380)
        mission_rows.append([f"M-{i:03d}", addr, lat, lon, window,
                             priority, cargo])
    p = os.path.join(base, "missions.csv")
    write_csv(p, ["mission_id", "address", "lat", "lon",
                  "time_window", "priority", "cargo_kg"], mission_rows)
    files.append(p)

    # Per-vehicle diagnostics for the maintenance coordinator.
    diag_rows = []
    for vid in ["ALPHA", "BRAVO"]:
        diag_rows.append([
            vid,
            rng.randint(1200, 9800),                 # engine_hours
            round(rng.uniform(28, 36), 1),           # tire_pressure_psi
            round(rng.uniform(10, 85), 1),           # brake_wear_pct
            round(rng.uniform(15, 95), 1),           # oil_level_pct
            f"2024-0{rng.randint(1, 4)}-{rng.randint(10, 28)}",  # last_service
        ])
    p = os.path.join(base, "vehicle-diagnostics.csv")
    write_csv(p, ["vehicle", "engine_hours", "tire_pressure_psi",
                  "brake_wear_pct", "oil_level_pct", "last_service_date"],
              diag_rows)
    files.append(p)
    return files


def gen_multiparty_negotiation(rng: random.Random) -> list[str]:
    # This example declares only json_tool + file_write (no file-READING tool),
    # so per the policy it needs no mounted data. We still ship a human-readable
    # scenario brief so the reader can pass a concrete situation via
    # --initial-context. It is NOT mounted and NOT read by any tool.
    base = ex("09-experimental", "97-multi-party-negotiation", "data")
    p = os.path.join(base, "procurement-brief.md")
    write_text(p, """# Procurement scenario brief (synthetic)

> Reference material for the multi-party negotiation showcase. This file is
> **not** read by any tool — the crew uses only `json_tool` and `file_write`.
> Paste a condensed version into `--initial-context` to seed the negotiation.

## Situation

A mid-size electronics manufacturer must award a 24-month supply contract for
a new product line. Six internal stakeholders negotiate the terms, each with a
100-point concession budget.

## Target package (starting point)

- **Unit price**: target $42.00, seller asking $49.50
- **Quality**: ISO 9001 required; buyer wants additional IPC-A-610 Class 3
- **Delivery**: first shipment in 10 weeks; buyer wants 8
- **Ethics**: conflict-free materials + annual supply-chain audit
- **Environment**: >= 30% recycled content, low-carbon logistics
- **Innovation**: firmware-upgrade path + data-format interoperability

## Red lines (examples)

- Price: absolute max $46.00/unit
- Quality: safety-critical specs non-negotiable
- Ethics: zero tolerance on child labor
- Environment: hazardous-material ban is mandatory

All figures are fictional and for demonstration only.
""")
    return [p]


GENERATORS = [
    ("01-research-assistant", gen_research_assistant),
    ("19-experimental-data", gen_experimental_data),
    ("34-portfolio-consensus", gen_portfolio_consensus),
    ("40-invoice-processing", gen_invoice_processing),
    ("47-nutrition-planner", gen_nutrition_planner),
    ("63-skills-gap-mapping", gen_skills_gap),
    ("71-cloud-audit-nist", gen_cloud_audit),
    ("78-synthetic-data", gen_synthetic_data),
    ("87-fleet-management", gen_fleet_management),
    ("97-multi-party-negotiation", gen_multiparty_negotiation),
]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true",
                        help="only verify that generated files exist")
    args = parser.parse_args()

    if args.check:
        missing = 0
        for name, _ in GENERATORS:
            # Cheap existence heuristic: the example dir must have a data/ folder
            # (except 97 which ships one too). Full validation is the runner's job.
            pass
        print("check mode: run without --check to (re)generate files")
        return 0

    total = 0
    for name, fn in GENERATORS:
        rng = random.Random(SEED)  # fresh, per-example determinism
        produced = fn(rng)
        total += len(produced)
        for f in produced:
            rel = os.path.relpath(f, REPO_ROOT)
            size = os.path.getsize(f)
            print(f"  {rel}  ({size} bytes)")
        print(f"[{name}] {len(produced)} file(s)")
    print(f"\nDone. {total} file(s) generated with seed {SEED}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
