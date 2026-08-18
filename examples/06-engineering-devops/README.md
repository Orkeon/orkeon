# 06 - Ingenierie & DevOps (66-75, 102-103)

Engineering and DevOps use cases: CI/CD pipelines, incident response, database migration, performance analysis, documentation, cloud audits, and chaos engineering.

**Runner**: `standard`

| # | Example | Process | Quality |
|---|---------|---------|---------|
| 66 | Pipeline CI/CD avec Rollback Automatique | Sequential | Fiabilite |
| 67 | Incident Response -- Humain Valide en Prod | Sequential | Securite |
| 68 | Migration de Base de Donnees -- Reprise Exacte | Sequential | Robustesse |
| 69 | Analyse de Performance -- 4 Couches Paralleles + OpenTelemetry | Parallel -> Sequential | Robustesse |
| 70 | Documentation Technique Versionnee comme du Code | Sequential | Simplicite |
| 71 | Audit Cloud Multi-Piliers -- NIST + Sanitization | Parallel -> Sequential | Securite |
| 72 | Test de Charge -- Trending Historique + Detection Regression | Sequential | Fiabilite |
| 73 | Refactoring Securise -- Sandbox + Analyse Securite | Sequential | Securite |
| 74 | Gestion des Dependances -- Priorisation CVE | Sequential | Fiabilite |
| 75 | Chaos Engineering -- Humain Approuve Chaque Injection | Sequential | Securite |
| 102 | Analyse de Codebase TypeScript et Plan de Migration Orkeon (crew scriptée) | Sequential (3 phases) | Robustesse |
| 103 | Analyse de Codebase TypeScript avec FSM et Circuit Breaker | Sequential + FSM | Robustesse |
