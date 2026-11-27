# Capstone Project Report

Report 1: Project Introduction

Hanoi, September 2026

## I. Record of Changes

| Date | A\*M, D | In charge | Change Description |
| :-: | :-: | :-: | :-- |
| 20/9/2026 | A | QuanNT | Create doc |
| 8/10/2026 | M | QuanNT | Rewrite Product Background, Existing Systems, Business Opportunity and Software Product Vision |
| 12/10/2026 | M | QuanNT | Narrow scope to 2 job positions (Teller, Sales Officer); make BPMS standalone (Excel import/export replaces external integrations); update actors, screens, use cases, non-screen functions, entities, NFRs, BRs, messages |

\*A - Added, M - Modified, D - Deleted

## II. Project Introduction

### 1. Overview

#### 1.1 Project information

- Project name: Banking Employee Work Management and Performance Evaluation System
- Project code: BPMS
- Group name: SEP490-G54
- Software type: Web App

#### 1.2 Project team

| Full Name | Role | Email | Mobile |
| :-- | :-: | :-- | :-- |
| Ngô Thế Quân | Leader | quanngothe205@gmail.com | 0386585456 |
| Nguyễn Tiến Thành | Member | yeming2311@gmail.com | 0325809475 |
| Trương Duy Mạnh | Member | truongduymanh2512@gmail.com | |
| Dương Xuân Phú | Member | phuvip1112005@gmail.com | 0383694214 |
| Nguyễn Tuấn Kiệt | Member | nnguyenkiet0512@gmail.com | 0965412442 |

### 2. Product background

Banks keep KPIs, daily work and performance evaluation in three separate places, and no verifiable data links them. Banks have KPIs. What they lack is a connection between goals, work and evaluation. Interviews with employees and managers at Techcombank and BIDV show the same pattern, although the tools differ. Managers give targets by private message or verbally, so nothing is recorded in a system. Work is assigned and followed up through Zalo, verbal instruction or an internal web app. Scores are decided at period end, mostly from final outputs and the manager's judgment. The KPI target, the confirmed actual, the completion rate and the employee's overall performance end up folded into one number, and nobody can explain or audit that score.

The current workflow runs as follows. Management sets business objectives and KPI targets for the period. Managers pass the targets to employees by private message or word of mouth. BIDV has an internal app for assigning work, but KPIs still reach employees informally. Employees report progress through Zalo or the web app. When a customer case needs another role, the handover is informal: a Teller contacts a customer, for example, then refers the case to a Sales Officer to offer products. At period end, results are collected and consolidated by hand, and the manager scores and ranks the employees.

This flow leaves five gaps:

- Targets are not broken down by period and person.
- Results have no checkable evidence.
- Handovers are not tracked.
- No one formally verifies a result before it counts.
- A final score cannot be traced back to specific tasks.

Closing these gaps takes one unbroken chain of data from goals to evaluation: Business Objective → KPI Definition → Target Allocation → Task Assignment → Execution and Handover → Result with Evidence → Verification → KPI Contribution → Performance Evaluation → Ranking. Three principles shape the chain:

- Traceability. Every score traces backward to verified results, tasks and evidence.
- Separate concepts. Business Objective, KPI Definition, Target, Confirmed Actual, Completion Rate and Performance Evaluation are stored as distinct data.
- Role-aware evaluation. Criteria differ by position. Sales is measured by calls, meetings and conversion rate; Teller by transaction volume, speed and error rate.

BPMS implements this chain as a standalone web application for a bank branch. Release 1.0 covers two front-line job positions with clearly different work: Teller and Sales Officer (individual customers). Department Managers, the Branch Manager, QA/Inspectors, an HR Officer and a System Administrator support them.

Management defines objectives and KPIs and allocates targets, including those of business campaigns. Managers assign work from standard work types for each position, and employees also log their own work as they do it. Work is linked across positions: when a Teller hands a customer request to a Sales Officer, each person's contribution is recorded at the handover. Employees submit results with evidence and transaction references. Managers approve, reject or request revision after checking them against transaction data imported into the system. Only verified results become KPI actuals, and the final score follows the evaluation profile of each position.

BPMS exchanges data with other bank systems only through Excel files, so it can run without technical integration. A dashboard traces any score back to its work and evidence. Completing a task never grants points by itself. Banks get an objective, transparent and auditable basis for evaluating employees.

### 3. Existing systems

#### 3.1 SAP SuccessFactors Performance & Goals

SAP SuccessFactors Performance & Goals is a cloud performance management solution in SAP's human capital management suite. It helps organizations align employee goals with corporate objectives and run performance reviews with ongoing feedback. ([SAP product page](https://www.sap.com/products/hcm/performance-goals.html))

Main actors: Employee, Manager, HR Administrator.

Main features:

- Goal creation from templates or a goal library
- Goal cascading and sharing, to align employee, team and company goals
- Continuous feedback and coaching
- Performance assessments and 360-degree reviews
- Calibration of ratings across teams

Advantages:

- Goals stay aligned from company level down to each employee.
- It covers the whole review cycle, from goal setting to assessment.
- It is highly configurable and integrates with the wider HR suite.

Disadvantages:

- It is a large enterprise suite, usually implemented with the help of an implementation partner ([overview](https://www.michaelmanagement.com/blog/sap-successfactors-performance-and-goals-management)).
- It assesses performance mainly from goals, ratings and feedback, without task-level evidence or verified work results.
- It does not track task handovers between employees as a basis for each person's contribution.

#### 3.2 Betterworks

Betterworks is an enterprise platform for continuous performance management built around OKRs. It combines goals, feedback, coaching, calibration and analytics in one system, and its customers include financial services organizations. ([G2](https://g2.com/products/betterworks/reviews?page=9), [Software Finder](https://softwarefinder.com/hr/betterworks))

Main actors: Employee, Manager, HR.

Main features:

- Hierarchical objectives and key results, aligned across company, team and individual levels
- Regular check-ins and one-on-one conversations between managers and employees
- Feedback and recognition
- Calibration and performance review cycles
- Dashboards and analytics on goal progress
- Integrations with HR systems and collaboration tools

Advantages:

- Individual goals connect clearly to company strategy.
- Managers can see goal progress across business units.
- It works as a dedicated performance layer on top of an existing HR system ([review](https://peoplemanagingpeople.com/nl/tools/betterworks-beoordeling/)).

Disadvantages:

- It is built mainly for mid-sized and enterprise organizations and is not a full HR management platform.
- It centers on OKRs and check-ins. It does not assign daily tasks, collect evidence or verify results.
- It does not link an employee's score to approved results across a chain of handovers.

### 4. Business opportunity

Banks can move from passive KPI tracking to managing the activity funnel, where every step from input to performance is visible and verified. Bank data sits in Core Banking, CRM, spreadsheets and chat tools, and none of them links a KPI result to the people, tasks and evidence behind it.

The funnel BPMS manages is: Input → Assignment → Activity → Result → KPI Contribution → Performance.

| Opportunity | What BPMS enables | Who benefits |
| :-- | :-- | :-- |
| End-to-end visibility | Drill down from an aggregated KPI to the tasks, assignees and customers behind it. | Executives, managers |
| Role-specific evaluation | Measure effort and activity as well as final output, with a separate evaluation profile for Teller and Sales Officer. | Employees, HR |
| Reliable data | Replace unverified manual reports with results approved against evidence, and use handover chains to avoid double counting. | Managers, audit |
| Faster execution of strategy | Turn business objectives and campaigns into assignments for individual employees, and watch their contribution as it happens. | Executives, department managers |

BPMS works with Excel files that banks already produce, so a branch can adopt it without changing or connecting its core systems.

SAP SuccessFactors and Betterworks focus on goals and reviews. They do not cover task evidence, handovers or verification, which leaves room for a focused product built around traceable work and evaluation.

### 5. Software product vision

For executives, managers, operational staff and HR in banks who manage KPIs and work through chat and spreadsheets and judge performance on final outputs, the Banking Employee Work Management and Performance Evaluation System is a web platform that connects business objectives to daily work and evaluation. Separate task tools and end-of-period scoring leave gaps between steps. BPMS records each step as verifiable data, so every score traces to approved results and evidence.

The product delivers:

- One chain of data from objective to ranking, so every score can be explained.
- A view for each stakeholder: dashboards for executives, target and task tools for managers, one workspace for employees, and an auditable record for HR.
- Activity funnel management, for example Sales Officer from customer contact to meeting to sale, or Teller from transaction to referral.
- Task handover across roles, with each role's contribution recorded and no double counting.
- Role-based evaluation and data minimization: criteria per position, and only the data needed.

### 6. Project scope and limitations

#### 6.1 Major features

[Figure 1. Fishbone Diagram](https://drive.google.com/file/d/1lnFEgq9TgySSgnLl6-NuCCyLUeC6SxZW/view?usp=sharing)

FE-01: Organization data import and user access management

The System Administrator imports the organization structure, job positions and employee data, including employment status, from an Excel file exported from the bank's HRM, using a fixed template. The system validates the file, saves valid rows and reports error rows. Employees missing from a new file are not deleted; only a terminated status deactivates an account.

BPMS manages roles and access for Teller, Sales Officer, Department Manager, Branch Manager, QA/Inspector, HR Officer and System Administrator. Department Managers handle targets, work and approvals in their department. The Branch Manager has full business authority in the branch, including appeals. QA/Inspectors record risk events. The HR Officer views and exports locked evaluation records. Employees see only their own scores and an anonymous rank. An employee with unfinished work cannot be moved to another department, and one with a pending resignation receives no new targets or leads.

FE-02: Business objectives, KPI definitions and target allocation management

Defines business objectives, KPIs, metrics, weights and evaluation periods (month, quarter or year). Each KPI states its data source: verified work in BPMS or an imported file. The Branch Manager enters branch and department targets manually or imports them from Excel. Department Managers split monthly targets per employee, and the system suggests a daily target. Employees confirm or comment on their targets. A mid-period target change freezes the old target, creates a new one and recalculates progress. Approved leave propagates the target.

FE-03: Business campaign management

Distributes customer leads to employees within a timeframe and tracks daily actions on them. Campaigns carry no KPI points. KPI is measured on actual outcomes such as deposits, cards or loans. A lead is reserved for its assigned employee for a set time, and that employee keeps the credit even if someone else completes the deal. Reports separate campaign work from business-as-usual work.

FE-04: Work type, standard process and evaluation configuration

Defines work types for each job position with processes, expected results, completion and quality criteria, required evidence, and whether a transaction code applies. Maps each work type to a KPI metric. Configures one evaluation profile per position, with criteria, score group weights and product multipliers. Authorized users set all weights and multipliers; the system fixes none of them. For example, a Teller profile may use 40% Sales, 30% Service Quality and 30% Operations.

FE-05: Work planning, task assignment and tracking

Creates and assigns work from KPIs, campaigns or work plans, and lets Tellers and Sales Officers log their own work by the work types of their position. Work is tracked on a Kanban board with deadlines, priorities and status. Overdue work gets an Overdue status.

FE-06: Cross-position collaboration and task handover

Hands work over between positions and tracks the whole chain. A Teller hands requests above a configured amount (for example 50 million VND) or specific product requests to a Sales Officer, and refers prospective customers to Sales Officers. In a successful referral, the referrer and the processor each receive credit in their own KPI, and a result counts toward each KPI only once. A handover not accepted within a configured time (for example 4 hours) is escalated to the Department Manager.

FE-07: Result submission and work verification

Employees submit results with the required evidence and the transaction or customer code, timed by the server. The system matches the code against transaction data imported from Core Banking. The Department Manager approves, rejects or requests revision, and a result with a transaction code can be approved only when it matches an imported transaction. QA re-verifies a random sample of approved results. Employees can dispute a rejection within 48 hours. The Branch Manager decides, and the points stay out of rankings until then. Evidence of an approved result is locked.

FE-08: Result accumulation and KPI contribution recording

Converts verified results into KPI Actuals using the FE-04 mapping and this formula: sum of (Actual Progress / Target Assignment) x Task Multiplier. KPI Actuals from imported files (for example service ratings) are recorded with a link to their import batch. KPI Actuals cannot be entered manually, and a manager's actual is the sum of the results below. Errors found after a period closes are corrected by a negative entry in the current period.

FE-09: Performance evaluation and employee ranking

Calculates each employee's score from the weighted groups of their position, using configured formulas and no AI/ML. A manager assessment, with limited weight and a written reason, is shown separately. Risk events from QA/Inspectors reduce the Service Quality score and, for serious violations, lower the proposed rating (A1, A2, A3, B, C). Sales KPI has no cap.

Employees are ranked only against others in the same department and job position (Teller, Sales Officer). A mid-period change between these positions is evaluated per position profile.

The Branch Manager sets the number of A1, A2 and A3 slots each period. The system divides them among departments by their share of branch business results, then among position groups, and proposes ratings from the highest scores; ties go to the higher Business score. The Branch Manager confirms or adjusts with a reason. Closing a period locks its data.

FE-10: Overview dashboard, traceability, data import/export and audit

Shows workload, KPI and campaign progress, and pending approvals. Traces any score back through Business Objective → KPI/Campaign → Work → Result → Verification (and imported transaction) → KPI Contribution → Performance → Ranking. Provides a Data Import Center with downloadable templates and an import history with error rows. Exports evaluation and ranking reports as xlsx/PDF for the HR Officer and audit. Keeps an append-only audit log, and alerts the Department Manager when an imported service rating is 1 star.

#### 6.2 Limitations and exclusions

LI-1: The system does not include Core Banking capabilities such as general ledger accounting, payment posting, loan origination, credit scoring, or KYC/AML/fraud detection. Results are verified by manager approval on evidence and, for work with a transaction code, against transaction data imported from Excel.

LI-2: The system is not an HRM platform. Recruitment, attendance, leave, payroll and compensation/bonus calculations are out of scope, as are incentive caps, disciplinary measures and retraining decisions. Ratings and rankings do not change pay.

LI-3: The system does not use AI/ML to determine performance scores or HR decisions such as promotions, discipline or dismissal. Scores follow configured formulas. The system proposes ratings and the Branch Manager confirms them.

LI-4: The system is not an enterprise project management suite (Jira or MS Project capabilities such as sprint planning, resource leveling or Gantt analysis). Task tracking is limited to the Kanban board.

LI-5: The system does not integrate with Core Banking, CRM, DWH, HRM, Queue Ticket Dispenser (JCM/VCM), survey or IAM systems, in real time or by API. Data enters and leaves only through Excel files with fixed templates: employee data, transaction data and service ratings are imported, and evaluation and ranking reports are exported. Imported data is only as current as the last import, so results waiting for transaction data stay in an "In Review" state.

LI-6: Executing or completing a task on the Kanban board does not automatically grant performance points. Contributions come only from verified work results under defined verification and mapping policies.

LI-7: The system is a web application only, with no mobile app and no Zalo or email integration. Notifications appear inside the system.

LI-8: The system does not store sensitive customer data such as account or identity card numbers. It keeps only the customer ID and transaction code needed to link a result to its source.

LI-9: The system is designed for a general bank structure (branch, departments, employees). It is not tailored to a specific bank and does not support multiple banks.

LI-10: Release 1.0 supports only two job positions: Teller and Sales Officer (individual customers). Other positions (corporate relationship managers, back office, treasury, etc.), queue management and priority-customer services are out of scope. Job positions and evaluation profiles are configurable, so later releases can add positions.
