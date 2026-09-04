/// <reference orkeon-script="1.0" />
// orkeon-example: {"process":"sequential","agents":5,"tasks":5,"tools":["http_api","csv_reader","json_tool","relational_database_query","file_write","fundamental_data","factor_exposure","var_calculation"]}
//
// 33. Scoring de Credit Validation Humaine — a sequential pipeline with a
// conditionally triggered human validation (when the score crosses a risk
// threshold). Source: project/marketing/content-strategy/101-USE-CASES.md #33
// Built-in tools are referenced by name; the trading tools come as TypeScript
// instances from the shared _tools module (EX-01).

import { pickTools } from "../_tools/index.ts";

const collector = agentBuilder()
    .name("collector")
    .role("Credit Data Collector")
    .goal("Gather applicant financial data from credit bureaus and internal sources")
    .backstory(`Data collection specialist with access to major credit bureaus (Experian, Equifax, TransUnion).
Aggregates credit history, payment records, outstanding debts, and employment verification
into a standardized applicant profile.`)
    .tools(["http_api", "csv_reader", "json_tool"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const scorer = agentBuilder()
    .name("scorer")
    .role("Credit Scorer")
    .goal("Calculate credit score using multiple scoring models and generate risk assessment")
    .backstory(`Quantitative analyst specializing in credit scoring models. Applies multiple
methodologies (logistic regression, decision trees, ensemble models) to produce
a composite score with confidence intervals and key risk factors.`)
    .tools(["json_tool", "csv_reader"])
    .withAutonomousTools(pickTools("fundamental_data"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const riskAnalyst = agentBuilder()
    .name("risk_analyst")
    .role("Risk Analyst")
    .goal("Analyze edge cases and provide detailed risk commentary for borderline applications")
    .backstory(`Senior credit risk analyst with 15 years experience in consumer and commercial lending.
Specializes in analyzing borderline cases where automated scoring may be insufficient.
Provides detailed qualitative risk commentary.`)
    .tools(["json_tool", "relational_database_query", "file_write"])
    .withAutonomousTools(pickTools("factor_exposure", "var_calculation"))
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const recommender = agentBuilder()
    .name("recommender")
    .role("Credit Decision Recommender")
    .goal("Synthesize scoring and risk analysis into a final credit recommendation")
    .backstory(`Decision scientist who integrates quantitative scores with qualitative risk factors
to produce a final recommendation. Determines whether human review is required
based on risk thresholds and policy rules.`)
    .tools(["json_tool", "file_write"])
    .allowDelegation(false)
    .maxIterations(10)
    .verbose(true)
    .build();

const humanDecider = agentBuilder()
    .name("human_decider")
    .role("Human Credit Decider")
    .goal("Review and approve or reject high-risk credit applications that exceed automated thresholds")
    .backstory(`Senior credit officer responsible for final decisions on applications that
exceed automated risk thresholds. Reviews all supporting documentation
and risk commentary before making a binding decision.`)
    .tools(["json_tool"])
    .allowDelegation(false)
    .maxIterations(5)
    .verbose(true)
    .build();

const collectData = taskBuilder()
    .name("collect_data")
    .agent(collector)
    .description("Collect applicant financial data from credit bureaus, employment verification services, and internal databases. Produce a standardized applicant profile.")
    .expectedOutput("JSON applicant profile with credit history, income verification, debt-to-income ratio, and employment status")
    .build();

const calculateScore = taskBuilder()
    .name("calculate_score")
    .agent(scorer)
    .description("Apply multiple credit scoring models to the applicant profile. Calculate composite score, confidence interval, and identify top risk factors.")
    .expectedOutput("JSON scoring report with composite score (0-850), confidence interval, risk factors ranked by impact, and model agreement metrics")
    .withContext(collectData)
    .build();

const analyzeRisk = taskBuilder()
    .name("analyze_risk")
    .agent(riskAnalyst)
    .description("Perform qualitative risk analysis on the scored application. Evaluate factors not captured by automated models: industry trends, local economic conditions, applicant narrative.")
    .expectedOutput("Detailed risk commentary with qualitative factors, mitigating circumstances, and overall risk rating (Low/Medium/High/Critical)")
    .withContext(calculateScore)
    .build();

const generateRecommendation = taskBuilder()
    .name("generate_recommendation")
    .agent(recommender)
    .description("Synthesize quantitative score and qualitative risk analysis into a final recommendation. Flag for human review if composite score is below 620 or risk rating is High/Critical.")
    .expectedOutput("Final recommendation (Approve/Conditional/Decline/Refer-to-Human) with supporting rationale and conditions")
    .withContext(analyzeRisk)
    .build();

const humanReview = taskBuilder()
    .name("human_review")
    .agent(humanDecider)
    .description("Review the complete application package for cases referred for human decision. Approve, modify conditions, or decline with documented rationale.")
    .expectedOutput("Final human decision with rationale, any modified conditions, and audit-compliant documentation")
    .withContext(generateRecommendation)
    .humanInput(true)
    .build();

const crew = crewBuilder()
    .name("credit-scoring")
    .goal("Evaluate credit applications with automated scoring and conditional human validation for high-risk cases")
    .process("sequential")
    .memory(true)
    .verbose(true)
    .withAgents([collector, scorer, riskAnalyst, recommender, humanDecider])
    .withTasks([collectData, calculateScore, analyzeRisk, generateRecommendation, humanReview])
    .build();

(globalThis as any).crew = crew;
