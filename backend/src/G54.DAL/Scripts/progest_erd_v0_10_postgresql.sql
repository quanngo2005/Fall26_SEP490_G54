-- ============================================================================
-- Progest - ERD v0.10 logical/physical schema
-- PostgreSQL
--
-- Source basis:
--   1) ERD Diagram v0.10 (ERD Context)
--   2) Decisions confirmed in conversation through 2026-10-04
--
-- Notes:
--   - All PKs use UUID.
--   - All tables have created_at / updated_at.
--   - Status/type concepts use PostgreSQL ENUMs.
--   - Mapping entities use their own UUID PK plus UNIQUE pair constraints.
--   - Cross-row business rules that cannot be expressed by normal FK/CHECK
--     are implemented with triggers where practical.
--   - No ObjectiveKPI -> TargetAssignment FK: a target is intentionally not
--     tied to a particular Objective.
-- ============================================================================

-- DROP old/starter tables if any
DROP TABLE IF EXISTS users CASCADE;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ============================================================================
-- ENUM TYPES
-- ============================================================================

CREATE TYPE department_level_type AS ENUM ('BRANCH', 'DEPARTMENT', 'TEAM');
CREATE TYPE employee_status AS ENUM ('ACTIVE', 'INACTIVE', 'ON_LEAVE', 'TERMINATED');
CREATE TYPE record_status AS ENUM ('ACTIVE', 'INACTIVE', 'ARCHIVED');
CREATE TYPE business_objective_status AS ENUM ('DRAFT', 'ACTIVE', 'CLOSED', 'CANCELLED');
CREATE TYPE business_campaign_status AS ENUM ('PLANNED', 'RUNNING', 'PAUSED', 'COMPLETED', 'CANCELLED');
CREATE TYPE kpi_status AS ENUM ('DRAFT', 'ACTIVE', 'INACTIVE', 'ARCHIVED');
CREATE TYPE kpi_aggregation_type AS ENUM ('SUM', 'RATIO', 'AVG', 'NO_AGGREGATION');
CREATE TYPE kpi_direction AS ENUM ('HIGHER_BETTER', 'LOWER_BETTER', 'ON_TARGET');
CREATE TYPE work_priority AS ENUM ('LOW', 'MEDIUM', 'HIGH', 'CRITICAL');
CREATE TYPE work_status AS ENUM ('NEW', 'IN_PROGRESS', 'HANDED_OVER', 'SUBMITTED', 'VERIFIED', 'REJECTED', 'DONE', 'CANCELLED');
CREATE TYPE workflow_definition_status AS ENUM ('DRAFT', 'ACTIVE', 'INACTIVE', 'ARCHIVED');
CREATE TYPE handover_status AS ENUM ('PENDING', 'ACCEPTED', 'REJECTED', 'CANCELLED');
CREATE TYPE submission_status AS ENUM ('DRAFT', 'SUBMITTED', 'UNDER_REVIEW', 'APPROVED', 'REJECTED', 'WITHDRAWN');
CREATE TYPE verification_decision AS ENUM ('PENDING', 'APPROVED', 'REJECTED', 'NEED_MORE_INFO');
CREATE TYPE verified_result_status AS ENUM ('VALID', 'INVALID');
CREATE TYPE actual_progress_status AS ENUM ('VALID', 'INVALID');
CREATE TYPE risk_event_severity AS ENUM ('LOW', 'MEDIUM', 'HIGH', 'CRITICAL');
CREATE TYPE risk_event_status AS ENUM ('OPEN', 'CONFIRMED', 'DISMISSED', 'RESOLVED');
CREATE TYPE appeal_status AS ENUM ('OPEN', 'UNDER_REVIEW', 'ACCEPTED', 'REJECTED', 'WITHDRAWN');
CREATE TYPE period_type AS ENUM ('YEAR', 'HALF_YEAR', 'QUARTER', 'MONTH', 'DAY');
CREATE TYPE period_status AS ENUM ('OPEN', 'IN_REVIEW', 'LOCKED', 'CLOSED');
CREATE TYPE evaluation_profile_status AS ENUM ('DRAFT', 'ACTIVE', 'INACTIVE', 'ARCHIVED');
CREATE TYPE performance_evaluation_status AS ENUM ('DRAFT', 'CALCULATED', 'IN_REVIEW', 'APPROVED', 'DISPUTED', 'FINAL');
CREATE TYPE target_assignment_status AS ENUM ('DRAFT', 'ACTIVE', 'SUPERSEDED', 'CLOSED');
CREATE TYPE target_split_dimension AS ENUM ('ORG', 'PERIOD');

-- ============================================================================
-- 1. ORGANIZATION & ACCESS
-- ============================================================================

CREATE TABLE department (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code                VARCHAR(50) NOT NULL,
    name                VARCHAR(200) NOT NULL,
    description         TEXT,
    parent_department_id UUID,
    level_type          department_level_type NOT NULL,
    status              record_status NOT NULL DEFAULT 'ACTIVE',
    last_sync_at	TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_department_code UNIQUE (code),
    CONSTRAINT fk_department_parent
        FOREIGN KEY (parent_department_id)
        REFERENCES department(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_department_no_self_parent
        CHECK (parent_department_id IS NULL OR parent_department_id <> id)
);

CREATE INDEX ix_department_parent ON department(parent_department_id);

CREATE TABLE job_position (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code                  VARCHAR(50) NOT NULL,
    title                 VARCHAR(200) NOT NULL,
    description           TEXT,
    evaluation_profile_id UUID NOT NULL,
    status                record_status NOT NULL DEFAULT 'ACTIVE',
    last_sync_at          TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_job_position_code UNIQUE (code)
);

CREATE TABLE employee (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_code     VARCHAR(50) NOT NULL,
    full_name         VARCHAR(200) NOT NULL,
    email             VARCHAR(255) NOT NULL,
    phone             VARCHAR(30),
    department_id     UUID NOT NULL,
    job_position_id   UUID NOT NULL,
    manager_id        UUID,
    status            employee_status NOT NULL DEFAULT 'ACTIVE',
    last_sync_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_employee_code UNIQUE (employee_code),
    CONSTRAINT uq_employee_email UNIQUE (email),
    CONSTRAINT fk_employee_department
        FOREIGN KEY (department_id)
        REFERENCES department(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_employee_job_position
        FOREIGN KEY (job_position_id)
        REFERENCES job_position(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_employee_manager
        FOREIGN KEY (manager_id)
        REFERENCES employee(id)
        ON DELETE SET NULL,
    CONSTRAINT ck_employee_no_self_manager
        CHECK (manager_id IS NULL OR manager_id <> id)
);

CREATE INDEX ix_employee_department ON employee(department_id);
CREATE INDEX ix_employee_job_position ON employee(job_position_id);
CREATE INDEX ix_employee_manager ON employee(manager_id);

CREATE TABLE account (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id    UUID NOT NULL,
    username       VARCHAR(100) NOT NULL,
    password_hash  VARCHAR(255) NOT NULL,
    is_locked      BOOLEAN NOT NULL DEFAULT FALSE,
    last_sync_at   TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_account_employee UNIQUE (employee_id),
    CONSTRAINT uq_account_username UNIQUE (username),
    CONSTRAINT fk_account_employee
        FOREIGN KEY (employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT
);

CREATE TABLE role (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code        VARCHAR(50) NOT NULL,
    name        VARCHAR(150) NOT NULL,
    description TEXT,
    status      record_status NOT NULL DEFAULT 'ACTIVE',
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_role_code UNIQUE (code)
);

CREATE TABLE permission (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code        VARCHAR(100) NOT NULL,
    name        VARCHAR(150) NOT NULL,
    resource    VARCHAR(100) NOT NULL,
    action      VARCHAR(50) NOT NULL,
    description TEXT,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_permission_code UNIQUE (code),
    CONSTRAINT uq_permission_resource_action UNIQUE (resource, action)
);

CREATE TABLE user_role (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    account_id  UUID NOT NULL,
    role_id     UUID NOT NULL,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_user_role UNIQUE (account_id, role_id),
    CONSTRAINT fk_user_role_account
        FOREIGN KEY (account_id)
        REFERENCES account(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_user_role_role
        FOREIGN KEY (role_id)
        REFERENCES role(id)
        ON DELETE RESTRICT
);

CREATE TABLE role_permission (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    role_id       UUID NOT NULL,
    permission_id UUID NOT NULL,
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_role_permission UNIQUE (role_id, permission_id),
    CONSTRAINT fk_role_permission_role
        FOREIGN KEY (role_id)
        REFERENCES role(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_role_permission_permission
        FOREIGN KEY (permission_id)
        REFERENCES permission(id)
        ON DELETE CASCADE
);

-- ============================================================================
-- 2. STRATEGY / KPI
-- ============================================================================

CREATE TABLE business_objective (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code        VARCHAR(50) NOT NULL,
    name        VARCHAR(255) NOT NULL,
    description TEXT,
    start_date  DATE NOT NULL,
    end_date    DATE NOT NULL,
    status      business_objective_status NOT NULL DEFAULT 'DRAFT',
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_business_objective_code UNIQUE (code),
    CONSTRAINT ck_business_objective_dates CHECK (end_date >= start_date)
);

CREATE TABLE business_campaign (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    business_objective_id UUID NOT NULL,
    code                  VARCHAR(50) NOT NULL,
    name                  VARCHAR(255) NOT NULL,
    description           TEXT,
    start_date            DATE NOT NULL,
    end_date              DATE NOT NULL,
    status                business_campaign_status NOT NULL DEFAULT 'PLANNED',
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_business_campaign_code UNIQUE (code),
    CONSTRAINT fk_business_campaign_objective
        FOREIGN KEY (business_objective_id)
        REFERENCES business_objective(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_business_campaign_dates CHECK (end_date >= start_date)
);

CREATE TABLE kpi_group (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code        VARCHAR(50) NOT NULL,
    name        VARCHAR(200) NOT NULL,
    description TEXT,
    status      kpi_status NOT NULL DEFAULT 'ACTIVE',
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_kpi_group_code UNIQUE (code)
);

CREATE TABLE kpi_definition (
    id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    kpi_group_id           UUID NOT NULL,
    code                   VARCHAR(50) NOT NULL,
    name                   VARCHAR(255) NOT NULL,
    description            TEXT,
    formula                TEXT,
    unit                   VARCHAR(50) NOT NULL,
    aggregation_type       kpi_aggregation_type NOT NULL,
    direction              kpi_direction NOT NULL,
    max_achievement_percent NUMERIC(8,4),
    status                 kpi_status NOT NULL DEFAULT 'ACTIVE',
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_kpi_definition_code UNIQUE (code),
    CONSTRAINT fk_kpi_definition_group
        FOREIGN KEY (kpi_group_id)
        REFERENCES kpi_group(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_kpi_max_achievement
        CHECK (max_achievement_percent IS NULL OR max_achievement_percent > 0)
);

-- N:N: BusinessObjective <-> KPIDefinition
CREATE TABLE objective_kpi (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    business_objective_id UUID NOT NULL,
    kpi_definition_id  UUID NOT NULL,
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_objective_kpi UNIQUE (business_objective_id, kpi_definition_id),
    CONSTRAINT fk_objective_kpi_objective
        FOREIGN KEY (business_objective_id)
        REFERENCES business_objective(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_objective_kpi_kpi
        FOREIGN KEY (kpi_definition_id)
        REFERENCES kpi_definition(id)
        ON DELETE RESTRICT
);

-- ============================================================================
-- 3. CAMPAIGN / WORK TYPE CATALOG / MAPPINGS
-- ============================================================================

CREATE TABLE participant (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    business_campaign_id  UUID NOT NULL,
    employee_id           UUID NOT NULL,
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_participant UNIQUE (business_campaign_id, employee_id),
    CONSTRAINT fk_participant_campaign
        FOREIGN KEY (business_campaign_id)
        REFERENCES business_campaign(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_participant_employee
        FOREIGN KEY (employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT
);

CREATE TABLE campaign_work_type (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    business_campaign_id  UUID NOT NULL,
    work_type_id          UUID NOT NULL,
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_campaign_work_type UNIQUE (business_campaign_id, work_type_id)
);

CREATE TABLE job_position_work_type (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    job_position_id  UUID NOT NULL,
    work_type_id     UUID NOT NULL,
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_job_position_work_type UNIQUE (job_position_id, work_type_id)
);

CREATE TABLE workflow_definition (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code        VARCHAR(50) NOT NULL,
    name        VARCHAR(200) NOT NULL,
    description TEXT,
    version     INTEGER NOT NULL DEFAULT 1,
    status      workflow_definition_status NOT NULL DEFAULT 'ACTIVE',
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_workflow_definition_code_version UNIQUE (code, version),
    CONSTRAINT ck_workflow_definition_version CHECK (version > 0)
);

CREATE TABLE work_type (
    id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code                   VARCHAR(50) NOT NULL,
    name                   VARCHAR(200) NOT NULL,
    description            TEXT,
    workflow_definition_id UUID NOT NULL,
    status                 record_status NOT NULL DEFAULT 'ACTIVE',
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_work_type_code UNIQUE (code),
    CONSTRAINT fk_work_type_workflow_definition
        FOREIGN KEY (workflow_definition_id)
        REFERENCES workflow_definition(id)
        ON DELETE RESTRICT
);

-- N:N applicability: WorkType <-> KPIDefinition
CREATE TABLE work_type_kpi (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    work_type_id      UUID NOT NULL,
    kpi_definition_id UUID NOT NULL,
    is_required       BOOLEAN NOT NULL DEFAULT FALSE,
    effective_from    DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_to      DATE,
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_work_type_kpi_effective UNIQUE (work_type_id, kpi_definition_id, effective_from),
    CONSTRAINT fk_work_type_kpi_work_type
        FOREIGN KEY (work_type_id)
        REFERENCES work_type(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_work_type_kpi_kpi
        FOREIGN KEY (kpi_definition_id)
        REFERENCES kpi_definition(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_work_type_kpi_dates
        CHECK (effective_to IS NULL OR effective_to >= effective_from)
);

ALTER TABLE campaign_work_type
    ADD CONSTRAINT fk_campaign_work_type_campaign
        FOREIGN KEY (business_campaign_id)
        REFERENCES business_campaign(id)
        ON DELETE CASCADE;

ALTER TABLE campaign_work_type
    ADD CONSTRAINT fk_campaign_work_type_work_type
        FOREIGN KEY (work_type_id)
        REFERENCES work_type(id)
        ON DELETE CASCADE;

ALTER TABLE job_position_work_type
    ADD CONSTRAINT fk_job_position_work_type_position
        FOREIGN KEY (job_position_id)
        REFERENCES job_position(id)
        ON DELETE CASCADE;

ALTER TABLE job_position_work_type
    ADD CONSTRAINT fk_job_position_work_type_work_type
        FOREIGN KEY (work_type_id)
        REFERENCES work_type(id)
        ON DELETE CASCADE;

-- ============================================================================
-- 4. EVALUATION PROFILE / PERIOD
-- ============================================================================

CREATE TABLE evaluation_profile (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code           VARCHAR(50) NOT NULL,
    name           VARCHAR(200) NOT NULL,
    description    TEXT,
    version        INTEGER NOT NULL DEFAULT 1,
    effective_from DATE NOT NULL DEFAULT CURRENT_DATE,
    effective_to   DATE,
    status         evaluation_profile_status NOT NULL DEFAULT 'ACTIVE',
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_evaluation_profile_code_version UNIQUE (code, version),
    CONSTRAINT ck_evaluation_profile_version CHECK (version > 0),
    CONSTRAINT ck_evaluation_profile_dates
        CHECK (effective_to IS NULL OR effective_to >= effective_from)
);

ALTER TABLE job_position
    ADD CONSTRAINT fk_job_position_evaluation_profile
        FOREIGN KEY (evaluation_profile_id)
        REFERENCES evaluation_profile(id)
        ON DELETE RESTRICT;

CREATE UNIQUE INDEX uq_evaluation_profile_active_code
    ON evaluation_profile(code)
    WHERE status = 'ACTIVE';

CREATE TABLE evaluation_profile_item (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    evaluation_profile_id UUID NOT NULL,
    kpi_group_id          UUID NOT NULL,
    weight_percent        NUMERIC(7,4) NOT NULL,
    display_order         INTEGER NOT NULL DEFAULT 1,
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_evaluation_profile_item UNIQUE (evaluation_profile_id, kpi_group_id),
    CONSTRAINT fk_evaluation_profile_item_profile
        FOREIGN KEY (evaluation_profile_id)
        REFERENCES evaluation_profile(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_evaluation_profile_item_group
        FOREIGN KEY (kpi_group_id)
        REFERENCES kpi_group(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_evaluation_profile_item_weight
        CHECK (weight_percent > 0 AND weight_percent <= 100),
    CONSTRAINT ck_evaluation_profile_item_display_order
        CHECK (display_order > 0)
);

CREATE TABLE evaluation_period (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    code               VARCHAR(50) NOT NULL,
    name               VARCHAR(150) NOT NULL,
    period_type        period_type NOT NULL,
    parent_period_id   UUID,
    start_date         DATE NOT NULL,
    end_date           DATE NOT NULL,
    status             period_status NOT NULL DEFAULT 'OPEN',
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_evaluation_period_code UNIQUE (code),
    CONSTRAINT fk_evaluation_period_parent
        FOREIGN KEY (parent_period_id)
        REFERENCES evaluation_period(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_evaluation_period_dates CHECK (end_date >= start_date),
    CONSTRAINT ck_evaluation_period_no_self_parent
        CHECK (parent_period_id IS NULL OR parent_period_id <> id)
);

CREATE INDEX ix_evaluation_period_parent ON evaluation_period(parent_period_id);

-- ============================================================================
-- 5. TARGET ASSIGNMENT
-- ============================================================================

CREATE TABLE target_assignment (
    id                      UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    kpi_definition_id       UUID NOT NULL,
    department_id           UUID,
    employee_id             UUID,
    evaluation_period_id    UUID NOT NULL,
    parent_assignment_id    UUID,
    version                 INTEGER NOT NULL DEFAULT 1,
    target_value            NUMERIC(18,4) NOT NULL,
    status                  target_assignment_status NOT NULL DEFAULT 'DRAFT',
    assigned_by             UUID,
    assigned_at             TIMESTAMPTZ,
    approved_by             UUID,
    approved_at             TIMESTAMPTZ,
    previous_version_id     UUID,
    change_reason           TEXT,
    split_dimension         target_split_dimension,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT fk_target_assignment_kpi
        FOREIGN KEY (kpi_definition_id)
        REFERENCES kpi_definition(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_target_assignment_department
        FOREIGN KEY (department_id)
        REFERENCES department(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_target_assignment_employee
        FOREIGN KEY (employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_target_assignment_period
        FOREIGN KEY (evaluation_period_id)
        REFERENCES evaluation_period(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_target_assignment_parent
        FOREIGN KEY (parent_assignment_id)
        REFERENCES target_assignment(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_target_assignment_previous_version
        FOREIGN KEY (previous_version_id)
        REFERENCES target_assignment(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_target_assignment_assigned_by
        FOREIGN KEY (assigned_by)
        REFERENCES employee(id)
        ON DELETE SET NULL,
    CONSTRAINT fk_target_assignment_approved_by
        FOREIGN KEY (approved_by)
        REFERENCES employee(id)
        ON DELETE SET NULL,
    CONSTRAINT ck_target_assignment_owner_xor
        CHECK ((department_id IS NOT NULL) <> (employee_id IS NOT NULL)),
    CONSTRAINT ck_target_assignment_version
        CHECK (version > 0),
    CONSTRAINT ck_target_assignment_value
        CHECK (target_value >= 0),
    CONSTRAINT ck_target_assignment_version_link
        CHECK (
            (version = 1 AND previous_version_id IS NULL)
            OR
            (version > 1 AND previous_version_id IS NOT NULL)
        ),
    CONSTRAINT ck_target_assignment_approval
        CHECK (
            (status IN ('ACTIVE', 'SUPERSEDED', 'CLOSED') AND approved_at IS NOT NULL)
            OR status = 'DRAFT'
        ),
    CONSTRAINT ck_target_assignment_approved_by_time
        CHECK (approved_at IS NULL OR approved_at >= created_at),
    CONSTRAINT ck_target_assignment_assigned_time
        CHECK (assigned_at IS NULL OR assigned_at >= created_at)
);

CREATE INDEX ix_target_assignment_period ON target_assignment(evaluation_period_id);
CREATE INDEX ix_target_assignment_kpi ON target_assignment(kpi_definition_id);
CREATE INDEX ix_target_assignment_parent ON target_assignment(parent_assignment_id);

CREATE UNIQUE INDEX uq_target_assignment_active_employee
    ON target_assignment(kpi_definition_id, employee_id, evaluation_period_id)
    WHERE employee_id IS NOT NULL AND status = 'ACTIVE';

CREATE UNIQUE INDEX uq_target_assignment_active_department
    ON target_assignment(kpi_definition_id, department_id, evaluation_period_id)
    WHERE department_id IS NOT NULL AND status = 'ACTIVE';

CREATE UNIQUE INDEX uq_target_assignment_version_employee
    ON target_assignment(kpi_definition_id, employee_id, evaluation_period_id, version)
    WHERE employee_id IS NOT NULL;

CREATE UNIQUE INDEX uq_target_assignment_version_department
    ON target_assignment(kpi_definition_id, department_id, evaluation_period_id, version)
    WHERE department_id IS NOT NULL;

-- ============================================================================
-- 6. WORK / HANDOVER / SUBMISSION / VERIFICATION / RESULT
-- ============================================================================

CREATE TABLE work (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    work_type_id          UUID,
    business_campaign_id  UUID,
    assignee_employee_id  UUID NOT NULL,
    code                  VARCHAR(50) NOT NULL,
    title                 VARCHAR(255) NOT NULL,
    description           TEXT,
    priority              work_priority NOT NULL DEFAULT 'MEDIUM',
    estimated_hours       NUMERIC(10,2),
    planned_start_date    DATE,
    planned_end_date      DATE,
    status                work_status NOT NULL DEFAULT 'NEW',
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_work_code UNIQUE (code),
    CONSTRAINT fk_work_work_type
        FOREIGN KEY (work_type_id)
        REFERENCES work_type(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_work_campaign
        FOREIGN KEY (business_campaign_id)
        REFERENCES business_campaign(id)
        ON DELETE SET NULL,
    CONSTRAINT fk_work_assignee
        FOREIGN KEY (assignee_employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_work_estimated_hours
        CHECK (estimated_hours IS NULL OR estimated_hours >= 0),
    CONSTRAINT ck_work_planned_dates
        CHECK (planned_end_date IS NULL OR planned_start_date IS NULL OR planned_end_date >= planned_start_date)
);

CREATE INDEX ix_work_work_type ON work(work_type_id);
CREATE INDEX ix_work_campaign ON work(business_campaign_id);
CREATE INDEX ix_work_assignee ON work(assignee_employee_id);

CREATE TABLE task_handover (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    work_id            UUID NOT NULL,
    from_employee_id   UUID NOT NULL,
    to_employee_id     UUID NOT NULL,
    reason             TEXT NOT NULL,
    handover_at        TIMESTAMPTZ NOT NULL DEFAULT now(),
    accepted_at        TIMESTAMPTZ,
    status             handover_status NOT NULL DEFAULT 'PENDING',
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT fk_task_handover_work
        FOREIGN KEY (work_id)
        REFERENCES work(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_task_handover_from_employee
        FOREIGN KEY (from_employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_task_handover_to_employee
        FOREIGN KEY (to_employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_task_handover_distinct
        CHECK (from_employee_id <> to_employee_id),
    CONSTRAINT ck_task_handover_accepted_at
        CHECK (accepted_at IS NULL OR accepted_at >= handover_at)
);

CREATE INDEX ix_task_handover_work ON task_handover(work_id);

CREATE TABLE result_submission (
    id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    work_id                UUID NOT NULL,
    submitter_employee_id  UUID NOT NULL,
    workflow_definition_id  UUID,
    submission_no          INTEGER NOT NULL DEFAULT 1,
    comment                TEXT,
    submitted_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    status                 submission_status NOT NULL DEFAULT 'SUBMITTED',
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_result_submission_attempt UNIQUE (work_id, submission_no),
    CONSTRAINT fk_result_submission_work
        FOREIGN KEY (work_id)
        REFERENCES work(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_result_submission_submitter
        FOREIGN KEY (submitter_employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_result_submission_workflow_definition
        FOREIGN KEY (workflow_definition_id)
        REFERENCES workflow_definition(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_result_submission_no CHECK (submission_no > 0)
);

CREATE INDEX ix_result_submission_work ON result_submission(work_id);

CREATE TABLE verification (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    result_submission_id  UUID NOT NULL,
    verifier_employee_id  UUID NOT NULL,
    round_no              INTEGER NOT NULL DEFAULT 1,
    decision              verification_decision NOT NULL DEFAULT 'PENDING',
    comment               TEXT,
    verified_at           TIMESTAMPTZ,
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_verification_round UNIQUE (result_submission_id, round_no),
    CONSTRAINT fk_verification_submission
        FOREIGN KEY (result_submission_id)
        REFERENCES result_submission(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_verification_verifier
        FOREIGN KEY (verifier_employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_verification_round CHECK (round_no > 0),
    CONSTRAINT ck_verification_time
        CHECK (
            (decision = 'PENDING' AND verified_at IS NULL)
            OR
            (decision <> 'PENDING' AND verified_at IS NOT NULL)
        )
);

CREATE INDEX ix_verification_submission ON verification(result_submission_id);

CREATE TABLE verified_work_result (
    id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    verification_id             UUID NOT NULL,
    invalidated_by_risk_event_id UUID,
    status                      verified_result_status NOT NULL DEFAULT 'VALID',
    verified_at                 TIMESTAMPTZ NOT NULL,
    invalidated_at              TIMESTAMPTZ,
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_verified_work_result_verification UNIQUE (verification_id),
    CONSTRAINT uq_verified_work_result_risk_event UNIQUE (invalidated_by_risk_event_id),
    CONSTRAINT fk_verified_work_result_verification
        FOREIGN KEY (verification_id)
        REFERENCES verification(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_verified_work_result_invalidated_at
        CHECK (
            (status = 'VALID' AND invalidated_at IS NULL)
            OR
            (status = 'INVALID' AND invalidated_at IS NOT NULL)
        )
);

CREATE INDEX ix_verified_work_result_status ON verified_work_result(status);

CREATE TABLE work_result_item (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    verified_work_result_id UUID NOT NULL,
    kpi_definition_id     UUID NOT NULL,
    value                 NUMERIC(18,4) NOT NULL,
    denominator           NUMERIC(18,4),
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_work_result_item_kpi UNIQUE (verified_work_result_id, kpi_definition_id),
    CONSTRAINT fk_work_result_item_verified_result
        FOREIGN KEY (verified_work_result_id)
        REFERENCES verified_work_result(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_work_result_item_kpi
        FOREIGN KEY (kpi_definition_id)
        REFERENCES kpi_definition(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_work_result_item_value CHECK (value >= 0),
    CONSTRAINT ck_work_result_item_denominator CHECK (denominator IS NULL OR denominator > 0)
);

CREATE INDEX ix_work_result_item_result ON work_result_item(verified_work_result_id);
CREATE INDEX ix_work_result_item_kpi ON work_result_item(kpi_definition_id);

-- ============================================================================
-- 7. RISK / APPEAL / EVIDENCE
-- ============================================================================

CREATE TABLE risk_event (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id   UUID NOT NULL,
    code          VARCHAR(50) NOT NULL,
    event_type    VARCHAR(50) NOT NULL,
    severity      risk_event_severity NOT NULL DEFAULT 'MEDIUM',
    description   TEXT NOT NULL,
    occurred_at   TIMESTAMPTZ NOT NULL,
    status        risk_event_status NOT NULL DEFAULT 'OPEN',
    resolved_at   TIMESTAMPTZ,
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_risk_event_code UNIQUE (code),
    CONSTRAINT fk_risk_event_employee
        FOREIGN KEY (employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_risk_event_resolved_at
        CHECK (resolved_at IS NULL OR resolved_at >= occurred_at)
);

CREATE INDEX ix_risk_event_employee ON risk_event(employee_id);

-- FK is added after risk_event exists.
ALTER TABLE verified_work_result
    ADD CONSTRAINT fk_verified_work_result_invalidator
        FOREIGN KEY (invalidated_by_risk_event_id)
        REFERENCES risk_event(id)
        ON DELETE RESTRICT;

CREATE TABLE appeal (
    id                     UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id            UUID NOT NULL,
    result_submission_id   UUID NOT NULL,
    reason                 TEXT NOT NULL,
    status                 appeal_status NOT NULL DEFAULT 'OPEN',
    filed_at               TIMESTAMPTZ NOT NULL DEFAULT now(),
    decided_at             TIMESTAMPTZ,
    note                   TEXT,
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT fk_appeal_employee
        FOREIGN KEY (employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_appeal_submission
        FOREIGN KEY (result_submission_id)
        REFERENCES result_submission(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_appeal_decided_at
        CHECK (decided_at IS NULL OR decided_at >= filed_at)
);

CREATE INDEX ix_appeal_submission ON appeal(result_submission_id);

CREATE TABLE evidence (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    result_submission_id  UUID,
    risk_event_id         UUID,
    appeal_id             UUID,
    file_name             VARCHAR(255) NOT NULL,
    file_path             VARCHAR(1000) NOT NULL,
    mime_type              VARCHAR(100),
    file_size             BIGINT,
    description            TEXT,
    uploaded_by            UUID NOT NULL,
    uploaded_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT fk_evidence_submission
        FOREIGN KEY (result_submission_id)
        REFERENCES result_submission(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_evidence_risk_event
        FOREIGN KEY (risk_event_id)
        REFERENCES risk_event(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_evidence_appeal
        FOREIGN KEY (appeal_id)
        REFERENCES appeal(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_evidence_uploader
        FOREIGN KEY (uploaded_by)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_evidence_single_owner
        CHECK (
            (CASE WHEN result_submission_id IS NOT NULL THEN 1 ELSE 0 END +
             CASE WHEN risk_event_id IS NOT NULL THEN 1 ELSE 0 END +
             CASE WHEN appeal_id IS NOT NULL THEN 1 ELSE 0 END) = 1
        ),
    CONSTRAINT ck_evidence_size
        CHECK (file_size IS NULL OR file_size > 0)
);

CREATE INDEX ix_evidence_submission ON evidence(result_submission_id);
CREATE INDEX ix_evidence_risk_event ON evidence(risk_event_id);
CREATE INDEX ix_evidence_appeal ON evidence(appeal_id);

-- ============================================================================
-- 8. KPI MEASUREMENT / ACTUAL PROGRESS
-- ============================================================================

CREATE TABLE actual_progress (
    id                           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    kpi_definition_id            UUID NOT NULL,
    employee_id                 UUID NOT NULL,
    evaluation_period_id         UUID NOT NULL,
    progress_date                DATE NOT NULL,
    value                        NUMERIC(18,4) NOT NULL,
    denominator                  NUMERIC(18,4),
    source_item_id               UUID,
    performance_contribution_id  UUID,
    status                       actual_progress_status NOT NULL DEFAULT 'VALID',
    updated_at                   TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT fk_actual_progress_kpi
        FOREIGN KEY (kpi_definition_id)
        REFERENCES kpi_definition(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_actual_progress_employee
        FOREIGN KEY (employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_actual_progress_period
        FOREIGN KEY (evaluation_period_id)
        REFERENCES evaluation_period(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_actual_progress_source_item
        FOREIGN KEY (source_item_id)
        REFERENCES work_result_item(id)
        ON DELETE SET NULL,
    CONSTRAINT uq_actual_progress_source_item UNIQUE (source_item_id),
    CONSTRAINT ck_actual_progress_value CHECK (value >= 0),
    CONSTRAINT ck_actual_progress_denominator CHECK (denominator IS NULL OR denominator > 0)
);

CREATE INDEX ix_actual_progress_kpi_period
    ON actual_progress(kpi_definition_id, evaluation_period_id);
CREATE INDEX ix_actual_progress_employee_period
    ON actual_progress(employee_id, evaluation_period_id);
CREATE INDEX ix_actual_progress_contribution
    ON actual_progress(performance_contribution_id);

-- ============================================================================
-- 9. PERFORMANCE EVALUATION / CONTRIBUTION
-- ============================================================================

CREATE TABLE performance_evaluation (
    id                    UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id           UUID NOT NULL,
    evaluation_period_id  UUID NOT NULL,
    evaluation_profile_id UUID NOT NULL,
    total_score           NUMERIC(10,4),
    status                performance_evaluation_status NOT NULL DEFAULT 'DRAFT',
    reviewer_employee_id  UUID,
    calculated_at         TIMESTAMPTZ,
    approved_at           TIMESTAMPTZ,
    comment               TEXT,
    updated_at            TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT uq_performance_evaluation UNIQUE (employee_id, evaluation_period_id),
    CONSTRAINT uq_performance_evaluation_identity UNIQUE (id, employee_id, evaluation_period_id),
    CONSTRAINT fk_performance_evaluation_employee
        FOREIGN KEY (employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_performance_evaluation_period
        FOREIGN KEY (evaluation_period_id)
        REFERENCES evaluation_period(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_performance_evaluation_profile
        FOREIGN KEY (evaluation_profile_id)
        REFERENCES evaluation_profile(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_performance_evaluation_reviewer
        FOREIGN KEY (reviewer_employee_id)
        REFERENCES employee(id)
        ON DELETE SET NULL
);

CREATE INDEX ix_performance_evaluation_period ON performance_evaluation(evaluation_period_id);
CREATE INDEX ix_performance_evaluation_profile ON performance_evaluation(evaluation_profile_id);

CREATE TABLE performance_contribution (
    id                         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    performance_evaluation_id  UUID NOT NULL,
    parent_contribution_id     UUID,
    employee_id                UUID NOT NULL,
    evaluation_period_id       UUID NOT NULL,
    kpi_definition_id          UUID,
    evaluation_profile_item_id UUID,
    target_assignment_id       UUID,
    target_value_snapshot      NUMERIC(18,4),
    target_version_snapshot    INTEGER,
    actual_value               NUMERIC(18,4),
    actual_denominator         NUMERIC(18,4),
    achievement_percent_raw    NUMERIC(10,4),
    achievement_percent_capped NUMERIC(10,4),
    weight_snapshot            NUMERIC(7,4),
    component_score            NUMERIC(10,4),
    updated_at                 TIMESTAMPTZ NOT NULL DEFAULT now(),
    CONSTRAINT fk_performance_contribution_evaluation
        FOREIGN KEY (performance_evaluation_id)
        REFERENCES performance_evaluation(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_performance_contribution_parent
        FOREIGN KEY (parent_contribution_id)
        REFERENCES performance_contribution(id)
        ON DELETE CASCADE,
    CONSTRAINT fk_performance_contribution_employee
        FOREIGN KEY (employee_id)
        REFERENCES employee(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_performance_contribution_period
        FOREIGN KEY (evaluation_period_id)
        REFERENCES evaluation_period(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_performance_contribution_kpi
        FOREIGN KEY (kpi_definition_id)
        REFERENCES kpi_definition(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_performance_contribution_profile_item
        FOREIGN KEY (evaluation_profile_item_id)
        REFERENCES evaluation_profile_item(id)
        ON DELETE RESTRICT,
    CONSTRAINT fk_performance_contribution_target
        FOREIGN KEY (target_assignment_id)
        REFERENCES target_assignment(id)
        ON DELETE RESTRICT,
    CONSTRAINT ck_performance_contribution_kind
        CHECK ((kpi_definition_id IS NOT NULL) <> (evaluation_profile_item_id IS NOT NULL)),
    CONSTRAINT ck_performance_contribution_kpi_level
        CHECK (
            kpi_definition_id IS NULL
            OR (
                parent_contribution_id IS NOT NULL
                AND target_assignment_id IS NOT NULL
                AND target_value_snapshot IS NOT NULL
                AND actual_value IS NOT NULL
                AND weight_snapshot IS NULL
            )
        ),
    CONSTRAINT ck_performance_contribution_group_level
        CHECK (
            evaluation_profile_item_id IS NULL
            OR (
                parent_contribution_id IS NULL
                AND target_assignment_id IS NULL
                AND target_value_snapshot IS NULL
                AND actual_value IS NULL
                AND weight_snapshot IS NOT NULL
                AND weight_snapshot > 0
                AND weight_snapshot <= 100
            )
        ),
    CONSTRAINT ck_performance_contribution_target_version
        CHECK (target_version_snapshot IS NULL OR target_version_snapshot > 0),
    CONSTRAINT ck_performance_contribution_denominator
        CHECK (actual_denominator IS NULL OR actual_denominator > 0)
);

CREATE UNIQUE INDEX uq_performance_contribution_kpi
    ON performance_contribution(performance_evaluation_id, kpi_definition_id)
    WHERE kpi_definition_id IS NOT NULL;

CREATE UNIQUE INDEX uq_performance_contribution_group
    ON performance_contribution(performance_evaluation_id, evaluation_profile_item_id)
    WHERE evaluation_profile_item_id IS NOT NULL;

CREATE INDEX ix_performance_contribution_parent ON performance_contribution(parent_contribution_id);
CREATE INDEX ix_performance_contribution_target ON performance_contribution(target_assignment_id);

ALTER TABLE performance_contribution
    ADD CONSTRAINT fk_performance_contribution_evaluation_identity
        FOREIGN KEY (performance_evaluation_id, employee_id, evaluation_period_id)
        REFERENCES performance_evaluation(id, employee_id, evaluation_period_id)
        ON DELETE CASCADE;

ALTER TABLE actual_progress
    ADD CONSTRAINT fk_actual_progress_contribution
        FOREIGN KEY (performance_contribution_id)
        REFERENCES performance_contribution(id)
        ON DELETE SET NULL;

-- ============================================================================
-- TRIGGER FUNCTIONS / BUSINESS INVARIANTS
-- ============================================================================

-- --------------------------------------------------------------------------
-- updated_at helper
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$;

DO $$
DECLARE
    t TEXT;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'department','job_position','employee','account','role','permission','user_role','role_permission',
        'business_objective','business_campaign','kpi_group','kpi_definition','objective_kpi',
        'participant','campaign_work_type','job_position_work_type','workflow_definition','work_type','work_type_kpi',
        'evaluation_profile','evaluation_profile_item','evaluation_period','target_assignment',
        'work','task_handover','result_submission','verification','verified_work_result','work_result_item',
        'risk_event','appeal','evidence','actual_progress','performance_evaluation','performance_contribution'
    ] LOOP
        EXECUTE format('DROP TRIGGER IF EXISTS trg_%I_updated_at ON %I', t, t);
        EXECUTE format('CREATE TRIGGER trg_%I_updated_at BEFORE UPDATE ON %I FOR EACH ROW EXECUTE FUNCTION set_updated_at()', t, t);
    END LOOP;
END;
$$;

-- --------------------------------------------------------------------------
-- EvaluationPeriod hierarchy:
-- YEAR -> HALF_YEAR -> QUARTER -> MONTH -> DAY
-- Parent must be immediately above child, and child's dates must be within parent.
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION validate_evaluation_period()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    p evaluation_period%ROWTYPE;
BEGIN
    IF NEW.period_type = 'YEAR' THEN
        IF NEW.parent_period_id IS NOT NULL THEN
            RAISE EXCEPTION 'YEAR period cannot have a parent';
        END IF;
        RETURN NEW;
    END IF;

    IF NEW.parent_period_id IS NULL THEN
        RAISE EXCEPTION 'Non-YEAR evaluation period must have a parent';
    END IF;

    SELECT * INTO p FROM evaluation_period WHERE id = NEW.parent_period_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'Parent evaluation period % not found', NEW.parent_period_id;
    END IF;

    IF NEW.period_type = 'HALF_YEAR' AND p.period_type <> 'YEAR' THEN
        RAISE EXCEPTION 'HALF_YEAR must be directly under YEAR';
    ELSIF NEW.period_type = 'QUARTER' AND p.period_type <> 'HALF_YEAR' THEN
        RAISE EXCEPTION 'QUARTER must be directly under HALF_YEAR';
    ELSIF NEW.period_type = 'MONTH' AND p.period_type <> 'QUARTER' THEN
        RAISE EXCEPTION 'MONTH must be directly under QUARTER';
    ELSIF NEW.period_type = 'DAY' AND p.period_type <> 'MONTH' THEN
        RAISE EXCEPTION 'DAY must be directly under MONTH';
    END IF;

    IF NEW.start_date < p.start_date OR NEW.end_date > p.end_date THEN
        RAISE EXCEPTION 'Child period dates must be contained within parent period';
    END IF;

    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER trg_validate_evaluation_period
AFTER INSERT OR UPDATE ON evaluation_period
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION validate_evaluation_period();

-- --------------------------------------------------------------------------
-- EvaluationProfileItem: active profile weight sum must equal 100.
-- Deferrable so all items can be created in one transaction.
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION validate_profile_weight_sum()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    profile_id UUID;
    total_weight NUMERIC(12,4);
    p_status evaluation_profile_status;
BEGIN
    profile_id := COALESCE(NEW.evaluation_profile_id, OLD.evaluation_profile_id);

    SELECT status INTO p_status
    FROM evaluation_profile
    WHERE id = profile_id;

    IF p_status = 'ACTIVE' THEN
        SELECT COALESCE(SUM(weight_percent), 0)
        INTO total_weight
        FROM evaluation_profile_item
        WHERE evaluation_profile_id = profile_id;

        IF total_weight <> 100 THEN
            RAISE EXCEPTION 'Active evaluation profile % must have total item weight = 100, got %',
                profile_id, total_weight;
        END IF;
    END IF;

    RETURN NULL;
END;
$$;

CREATE CONSTRAINT TRIGGER trg_validate_profile_weight_sum
AFTER INSERT OR UPDATE OR DELETE ON evaluation_profile_item
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION validate_profile_weight_sum();

CREATE OR REPLACE FUNCTION validate_profile_activation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    total_weight NUMERIC(12,4);
BEGIN
    IF NEW.status = 'ACTIVE' THEN
        SELECT COALESCE(SUM(weight_percent), 0)
        INTO total_weight
        FROM evaluation_profile_item
        WHERE evaluation_profile_id = NEW.id;

        IF total_weight <> 100 THEN
            RAISE EXCEPTION 'Cannot activate evaluation profile %: item weights must total 100, got %',
                NEW.id, total_weight;
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

CREATE CONSTRAINT TRIGGER trg_validate_profile_activation
AFTER INSERT OR UPDATE ON evaluation_profile
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION validate_profile_activation();

-- --------------------------------------------------------------------------
-- WorkTypeKPI mappings: same work type + KPI cannot have overlapping dates.
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION validate_work_type_kpi_dates()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM work_type_kpi x
        WHERE x.id <> NEW.id
          AND x.work_type_id = NEW.work_type_id
          AND x.kpi_definition_id = NEW.kpi_definition_id
          AND daterange(x.effective_from, COALESCE(x.effective_to + 1, 'infinity'::date), '[)')
              && daterange(NEW.effective_from, COALESCE(NEW.effective_to + 1, 'infinity'::date), '[)')
    ) THEN
        RAISE EXCEPTION 'Overlapping WorkTypeKPI effective periods for work_type % and KPI %',
            NEW.work_type_id, NEW.kpi_definition_id;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_validate_work_type_kpi_dates
BEFORE INSERT OR UPDATE ON work_type_kpi
FOR EACH ROW EXECUTE FUNCTION validate_work_type_kpi_dates();

-- --------------------------------------------------------------------------
-- TargetAssignment structural validation.
-- Rules:
--   * parent/child same KPI
--   * ORG split keeps period same and changes only org owner
--   * PERIOD split keeps owner same and changes only period
--   * child period is descendant when split_dimension=PERIOD
--   * no cycles
--   * previous version has same KPI / owner / period
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION evaluation_period_is_descendant(child_id UUID, ancestor_id UUID)
RETURNS BOOLEAN
LANGUAGE sql
STABLE
AS $$
    WITH RECURSIVE p AS (
        SELECT id, parent_period_id
        FROM evaluation_period
        WHERE id = child_id
        UNION ALL
        SELECT e.id, e.parent_period_id
        FROM evaluation_period e
        JOIN p ON e.id = p.parent_period_id
    )
    SELECT EXISTS (SELECT 1 FROM p WHERE id = ancestor_id);
$$;

CREATE OR REPLACE FUNCTION department_is_descendant(child_department UUID, ancestor_department UUID)
RETURNS BOOLEAN
LANGUAGE sql
STABLE
AS $$
    WITH RECURSIVE d AS (
        SELECT id, parent_department_id
        FROM department
        WHERE id = child_department
        UNION ALL
        SELECT x.id, x.parent_department_id
        FROM department x
        JOIN d ON x.id = d.parent_department_id
    )
    SELECT EXISTS (SELECT 1 FROM d WHERE id = ancestor_department);
$$;

CREATE OR REPLACE FUNCTION target_owner_matches(
    p_department UUID,
    p_employee UUID,
    c_department UUID,
    c_employee UUID
)
RETURNS BOOLEAN
LANGUAGE plpgsql
STABLE
AS $$
BEGIN
    -- ORG split is only valid from an organizational node (department/branch/team)
    -- to a different descendant organizational node or to an employee inside it.
    IF p_employee IS NOT NULL THEN
        RETURN FALSE;
    END IF;

    IF c_department IS NOT NULL THEN
        RETURN c_department <> p_department
           AND department_is_descendant(c_department, p_department);
    END IF;

    IF c_employee IS NOT NULL THEN
        RETURN EXISTS (
            SELECT 1
            FROM employee e
            WHERE e.id = c_employee
              AND department_is_descendant(e.department_id, p_department)
        );
    END IF;

    RETURN FALSE;
END;
$$;

CREATE OR REPLACE FUNCTION validate_target_assignment()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    p target_assignment%ROWTYPE;
    pv target_assignment%ROWTYPE;
BEGIN
    IF NEW.parent_assignment_id IS NOT NULL THEN
        IF NEW.parent_assignment_id = NEW.id THEN
            RAISE EXCEPTION 'TargetAssignment cannot be its own parent';
        END IF;

        SELECT * INTO p FROM target_assignment WHERE id = NEW.parent_assignment_id;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'Parent TargetAssignment % not found', NEW.parent_assignment_id;
        END IF;

        IF p.kpi_definition_id <> NEW.kpi_definition_id THEN
            RAISE EXCEPTION 'Parent and child TargetAssignment must use the same KPI';
        END IF;

        IF NEW.split_dimension IS NULL THEN
            RAISE EXCEPTION 'split_dimension is required when parent_assignment_id is set';
        END IF;

        IF NEW.split_dimension = 'PERIOD' THEN
            IF p.department_id IS DISTINCT FROM NEW.department_id
               OR p.employee_id IS DISTINCT FROM NEW.employee_id THEN
                RAISE EXCEPTION 'PERIOD split may change only the evaluation period';
            END IF;

            IF NEW.evaluation_period_id = p.evaluation_period_id
               OR NOT evaluation_period_is_descendant(NEW.evaluation_period_id, p.evaluation_period_id) THEN
                RAISE EXCEPTION 'PERIOD split must move to a different descendant evaluation period';
            END IF;
        ELSE
            IF p.evaluation_period_id <> NEW.evaluation_period_id THEN
                RAISE EXCEPTION 'ORG split must keep the same evaluation period';
            END IF;

            IF p.department_id IS NULL THEN
                RAISE EXCEPTION 'ORG split requires parent assignment to target a department';
            END IF;

            IF NOT target_owner_matches(
                p.department_id, p.employee_id,
                NEW.department_id, NEW.employee_id
            ) THEN
                RAISE EXCEPTION 'ORG split child must be in the organizational descendant branch of the parent';
            END IF;
        END IF;

        -- cycle protection: walk from parent upward in the assignment tree
        IF EXISTS (
            WITH RECURSIVE chain AS (
                SELECT id, parent_assignment_id
                FROM target_assignment
                WHERE id = NEW.parent_assignment_id
                UNION ALL
                SELECT t.id, t.parent_assignment_id
                FROM target_assignment t
                JOIN chain c ON t.id = c.parent_assignment_id
            )
            SELECT 1 FROM chain WHERE id = NEW.id
        ) THEN
            RAISE EXCEPTION 'TargetAssignment parent relationship would create a cycle';
        END IF;
    ELSE
        IF NEW.split_dimension IS NOT NULL THEN
            RAISE EXCEPTION 'split_dimension must be NULL when parent_assignment_id is NULL';
        END IF;
    END IF;

    IF NEW.previous_version_id IS NOT NULL THEN
        IF NEW.previous_version_id = NEW.id THEN
            RAISE EXCEPTION 'TargetAssignment cannot reference itself as previous version';
        END IF;

        SELECT * INTO pv FROM target_assignment WHERE id = NEW.previous_version_id;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'Previous TargetAssignment % not found', NEW.previous_version_id;
        END IF;

        IF pv.kpi_definition_id <> NEW.kpi_definition_id
           OR pv.evaluation_period_id <> NEW.evaluation_period_id
           OR pv.department_id IS DISTINCT FROM NEW.department_id
           OR pv.employee_id IS DISTINCT FROM NEW.employee_id
           OR NEW.version <> pv.version + 1 THEN
            RAISE EXCEPTION 'Previous target version must have same KPI/owner/period and immediately precede the new version';
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_validate_target_assignment
BEFORE INSERT OR UPDATE ON target_assignment
FOR EACH ROW EXECUTE FUNCTION validate_target_assignment();

-- Sum-child rule for SUM KPIs. It is deferred so a complete split can be made
-- in one transaction. Unsplit parents are allowed to exist without children.
CREATE OR REPLACE FUNCTION validate_target_children_sum()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    parent_id UUID;
    p_kpi kpi_aggregation_type;
    child_count INTEGER;
    child_sum NUMERIC(30,4);
    parent_target NUMERIC(30,4);
    p_status target_assignment_status;
BEGIN
    parent_id := COALESCE(NEW.parent_assignment_id, OLD.parent_assignment_id);
    IF parent_id IS NULL THEN
        RETURN NULL;
    END IF;

    SELECT k.aggregation_type, t.target_value, t.status
    INTO p_kpi, parent_target, p_status
    FROM target_assignment t
    JOIN kpi_definition k ON k.id = t.kpi_definition_id
    WHERE t.id = parent_id;

    IF NOT FOUND OR p_kpi <> 'SUM' OR p_status <> 'ACTIVE' THEN
        RETURN NULL;
    END IF;

    SELECT COUNT(*), COALESCE(SUM(target_value), 0)
    INTO child_count, child_sum
    FROM target_assignment
    WHERE parent_assignment_id = parent_id
      AND status = 'ACTIVE';

    IF child_count > 0 AND child_sum <> parent_target THEN
        RAISE EXCEPTION 'Active child targets (%) must sum to parent target (%) for SUM KPI on TargetAssignment %',
            child_sum, parent_target, parent_id;
    END IF;

    RETURN NULL;
END;
$$;

CREATE CONSTRAINT TRIGGER trg_validate_target_children_sum
AFTER INSERT OR UPDATE OR DELETE ON target_assignment
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW EXECUTE FUNCTION validate_target_children_sum();

-- --------------------------------------------------------------------------
-- WorkTypeKPI applicability / ratio rules for WorkResultItem
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION validate_work_result_item()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    v_work_type_id UUID;
    v_agg kpi_aggregation_type;
    v_submitted_at DATE;
BEGIN
    SELECT w.work_type_id, k.aggregation_type
    INTO v_work_type_id, v_agg
    FROM verified_work_result vwr
    JOIN verification v ON v.id = vwr.verification_id
    JOIN result_submission rs ON rs.id = v.result_submission_id
    JOIN work w ON w.id = rs.work_id
    JOIN kpi_definition k ON k.id = NEW.kpi_definition_id
    WHERE vwr.id = NEW.verified_work_result_id;

    IF v_work_type_id IS NULL THEN
        RAISE EXCEPTION 'Unclassified Work cannot generate WorkResultItem KPI data';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM work_type_kpi wtk
        JOIN verified_work_result vwr ON vwr.id = NEW.verified_work_result_id
        JOIN verification ver ON ver.id = vwr.verification_id
        WHERE wtk.work_type_id = v_work_type_id
          AND wtk.kpi_definition_id = NEW.kpi_definition_id
          AND ver.verified_at::date >= wtk.effective_from
          AND (wtk.effective_to IS NULL OR ver.verified_at::date <= wtk.effective_to)
    ) THEN
        RAISE EXCEPTION 'KPI % is not applicable to the WorkType of WorkResultItem %',
            NEW.kpi_definition_id, NEW.id;
    END IF;

    IF v_agg = 'RATIO' THEN
        IF NEW.denominator IS NULL OR NEW.denominator <= 0 THEN
            RAISE EXCEPTION 'RATIO KPI WorkResultItem requires denominator > 0';
        END IF;
    ELSE
        IF NEW.denominator IS NOT NULL THEN
            RAISE EXCEPTION 'Non-RATIO KPI WorkResultItem must not have denominator';
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_validate_work_result_item
BEFORE INSERT OR UPDATE ON work_result_item
FOR EACH ROW EXECUTE FUNCTION validate_work_result_item();

-- Immutable metric item once it has generated ActualProgress.
CREATE OR REPLACE FUNCTION prevent_work_result_item_source_edit()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM actual_progress WHERE source_item_id = OLD.id) THEN
        IF NEW.kpi_definition_id IS DISTINCT FROM OLD.kpi_definition_id
           OR NEW.value IS DISTINCT FROM OLD.value
           OR NEW.denominator IS DISTINCT FROM OLD.denominator THEN
            RAISE EXCEPTION 'WorkResultItem % cannot be changed after ActualProgress has been generated; create an adjustment result instead', OLD.id;
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_prevent_work_result_item_source_edit
BEFORE UPDATE ON work_result_item
FOR EACH ROW EXECUTE FUNCTION prevent_work_result_item_source_edit();

-- --------------------------------------------------------------------------
-- ResultSubmission workflow snapshot must match WorkType's workflow definition.
-- Unclassified Work may leave workflow_definition_id NULL.
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION validate_submission_workflow()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    wt_workflow UUID;
    wt_id UUID;
BEGIN
    SELECT w.work_type_id, wt.workflow_definition_id
    INTO wt_id, wt_workflow
    FROM work w
    LEFT JOIN work_type wt ON wt.id = w.work_type_id
    WHERE w.id = NEW.work_id;

    IF wt_id IS NULL THEN
        IF NEW.workflow_definition_id IS NOT NULL THEN
            RAISE EXCEPTION 'Unclassified Work cannot snapshot a WorkflowDefinition';
        END IF;
    ELSE
        IF NEW.workflow_definition_id IS DISTINCT FROM wt_workflow THEN
            RAISE EXCEPTION 'ResultSubmission.workflow_definition_id must snapshot the WorkflowDefinition of its WorkType';
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_validate_submission_workflow
BEFORE INSERT OR UPDATE ON result_submission
FOR EACH ROW EXECUTE FUNCTION validate_submission_workflow();

-- --------------------------------------------------------------------------
-- Verification lifecycle: once a submission is APPROVED, it is closed and no
-- further verification rounds are permitted.
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION enforce_verification_lifecycle()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    submission_status_value submission_status;
    approved_exists BOOLEAN;
BEGIN
    SELECT status INTO submission_status_value
    FROM result_submission
    WHERE id = NEW.result_submission_id;

    SELECT EXISTS (
        SELECT 1 FROM verification v
        WHERE v.result_submission_id = NEW.result_submission_id
          AND v.decision = 'APPROVED'
          AND v.id <> NEW.id
    ) INTO approved_exists;

    IF approved_exists OR submission_status_value = 'APPROVED' THEN
        RAISE EXCEPTION 'Approved ResultSubmission is closed and cannot be verified again';
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_enforce_verification_lifecycle
BEFORE INSERT OR UPDATE ON verification
FOR EACH ROW EXECUTE FUNCTION enforce_verification_lifecycle();

CREATE OR REPLACE FUNCTION close_submission_on_approval()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.decision = 'APPROVED' THEN
        UPDATE result_submission
        SET status = 'APPROVED', updated_at = now()
        WHERE id = NEW.result_submission_id;
    ELSIF NEW.decision = 'REJECTED' THEN
        UPDATE result_submission
        SET status = 'REJECTED', updated_at = now()
        WHERE id = NEW.result_submission_id
          AND status <> 'APPROVED';
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_close_submission_on_approval
AFTER INSERT OR UPDATE ON verification
FOR EACH ROW EXECUTE FUNCTION close_submission_on_approval();

-- VerifiedWorkResult may exist only for an APPROVED verification.
CREATE OR REPLACE FUNCTION validate_verified_work_result()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    d verification_decision;
BEGIN
    SELECT decision INTO d FROM verification WHERE id = NEW.verification_id;
    IF d <> 'APPROVED' THEN
        RAISE EXCEPTION 'VerifiedWorkResult can only be created for an APPROVED Verification';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_validate_verified_work_result
BEFORE INSERT OR UPDATE ON verified_work_result
FOR EACH ROW EXECUTE FUNCTION validate_verified_work_result();

-- RiskEvent invalidation: set the linked VerifiedWorkResult to INVALID.
CREATE OR REPLACE FUNCTION sync_verified_result_invalidation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.invalidated_by_risk_event_id IS NOT NULL THEN
        NEW.status := 'INVALID';
        NEW.invalidated_at := COALESCE(NEW.invalidated_at, now());
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_sync_verified_result_invalidation
BEFORE INSERT OR UPDATE ON verified_work_result
FOR EACH ROW EXECUTE FUNCTION sync_verified_result_invalidation();

-- When a VerifiedWorkResult becomes invalid, invalidate source ActualProgress.
CREATE OR REPLACE FUNCTION invalidate_source_actual_progress()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.status = 'INVALID' AND OLD.status IS DISTINCT FROM NEW.status THEN
        UPDATE actual_progress ap
        SET status = 'INVALID', updated_at = now()
        FROM work_result_item wri
        WHERE wri.verified_work_result_id = NEW.id
          AND ap.source_item_id = wri.id;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_invalidate_source_actual_progress
AFTER UPDATE OF status ON verified_work_result
FOR EACH ROW EXECUTE FUNCTION invalidate_source_actual_progress();

-- --------------------------------------------------------------------------
-- ActualProgress rules:
--   * RATIO requires denominator; non-RATIO must not have denominator.
--   * source item derives KPI, employee and progress date from approved verification.
--   * period_id must be the DAY containing progress_date.
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION validate_actual_progress()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    a kpi_aggregation_type;
    src_kpi UUID;
    src_employee UUID;
    src_date DATE;
BEGIN
    SELECT aggregation_type INTO a
    FROM kpi_definition
    WHERE id = NEW.kpi_definition_id;

    IF a = 'RATIO' THEN
        IF NEW.denominator IS NULL OR NEW.denominator <= 0 THEN
            RAISE EXCEPTION 'RATIO ActualProgress requires denominator > 0';
        END IF;
    ELSE
        IF NEW.denominator IS NOT NULL THEN
            RAISE EXCEPTION 'Non-RATIO ActualProgress must not have denominator';
        END IF;
    END IF;

    -- Every ActualProgress is stored at the DAY period level.
    IF NOT EXISTS (
        SELECT 1
        FROM evaluation_period ep
        WHERE ep.id = NEW.evaluation_period_id
          AND ep.period_type = 'DAY'
          AND NEW.progress_date BETWEEN ep.start_date AND ep.end_date
    ) THEN
        RAISE EXCEPTION 'ActualProgress.period_id must point to the DAY period containing progress_date';
    END IF;

    IF NEW.performance_contribution_id IS NOT NULL THEN
        IF NOT EXISTS (
            SELECT 1
            FROM performance_contribution pc
            WHERE pc.id = NEW.performance_contribution_id
              AND pc.kpi_definition_id = NEW.kpi_definition_id
              AND pc.employee_id = NEW.employee_id
              AND pc.evaluation_period_id = NEW.evaluation_period_id
        ) THEN
            RAISE EXCEPTION 'ActualProgress contribution must be a KPI-level contribution with matching KPI, employee and period';
        END IF;
    END IF;

    IF NEW.source_item_id IS NOT NULL THEN
        SELECT wri.kpi_definition_id,
               rs.submitter_employee_id,
               ver.verified_at::date
        INTO src_kpi, src_employee, src_date
        FROM work_result_item wri
        JOIN verified_work_result vwr ON vwr.id = wri.verified_work_result_id
        JOIN verification ver ON ver.id = vwr.verification_id
        JOIN result_submission rs ON rs.id = ver.result_submission_id
        JOIN work w ON w.id = rs.work_id
        WHERE wri.id = NEW.source_item_id;

        IF src_kpi IS NULL THEN
            RAISE EXCEPTION 'Source WorkResultItem % not found', NEW.source_item_id;
        END IF;

        IF src_kpi <> NEW.kpi_definition_id THEN
            RAISE EXCEPTION 'ActualProgress KPI must match its source WorkResultItem KPI';
        END IF;

        IF src_employee <> NEW.employee_id THEN
            RAISE EXCEPTION 'ActualProgress employee must match ResultSubmission submitter of the source item';
        END IF;

        IF src_date <> NEW.progress_date THEN
            RAISE EXCEPTION 'ActualProgress.progress_date must equal approved Verification date';
        END IF;

        NULL;
    END IF;

    IF NEW.status = 'VALID' AND NEW.source_item_id IS NOT NULL THEN
        IF EXISTS (
            SELECT 1
            FROM verified_work_result vwr
            JOIN work_result_item wri ON wri.verified_work_result_id = vwr.id
            WHERE wri.id = NEW.source_item_id
              AND vwr.status = 'INVALID'
        ) THEN
            RAISE EXCEPTION 'ActualProgress sourced from an INVALID VerifiedWorkResult cannot remain VALID';
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_validate_actual_progress
BEFORE INSERT OR UPDATE ON actual_progress
FOR EACH ROW EXECUTE FUNCTION validate_actual_progress();

-- --------------------------------------------------------------------------
-- PerformanceContribution structural and cross-table consistency.
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION validate_performance_contribution()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    pe performance_evaluation%ROWTYPE;
    parent_pc performance_contribution%ROWTYPE;
    target target_assignment%ROWTYPE;
    target_department UUID;
    profile_item_profile UUID;
BEGIN
    SELECT * INTO pe
    FROM performance_evaluation
    WHERE id = NEW.performance_evaluation_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'PerformanceEvaluation % not found', NEW.performance_evaluation_id;
    END IF;

    IF NEW.employee_id <> pe.employee_id OR NEW.evaluation_period_id <> pe.evaluation_period_id THEN
        RAISE EXCEPTION 'Contribution employee/period must match PerformanceEvaluation';
    END IF;

    IF NEW.kpi_definition_id IS NOT NULL THEN
        IF NEW.parent_contribution_id IS NULL THEN
            RAISE EXCEPTION 'KPI-level Contribution requires a Group-level parent';
        END IF;

        SELECT * INTO parent_pc
        FROM performance_contribution
        WHERE id = NEW.parent_contribution_id;

        IF NOT FOUND OR parent_pc.evaluation_profile_item_id IS NULL THEN
            RAISE EXCEPTION 'KPI-level Contribution parent must be a Group-level Contribution';
        END IF;

        IF parent_pc.performance_evaluation_id <> NEW.performance_evaluation_id THEN
            RAISE EXCEPTION 'Contribution parent must belong to the same PerformanceEvaluation';
        END IF;

        IF NEW.target_assignment_id IS NULL THEN
            RAISE EXCEPTION 'KPI-level Contribution requires target_assignment_id';
        END IF;

        SELECT * INTO target
        FROM target_assignment
        WHERE id = NEW.target_assignment_id;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'TargetAssignment % not found', NEW.target_assignment_id;
        END IF;

        IF target.kpi_definition_id <> NEW.kpi_definition_id
           OR target.evaluation_period_id <> NEW.evaluation_period_id THEN
            RAISE EXCEPTION 'Contribution target must use the same KPI and period';
        END IF;

        IF NEW.target_version_snapshot IS NOT NULL
           AND NEW.target_version_snapshot <> target.version THEN
            RAISE EXCEPTION 'target_version_snapshot must equal TargetAssignment.version';
        END IF;

        IF NEW.target_value_snapshot IS NOT NULL
           AND NEW.target_value_snapshot <> target.target_value THEN
            RAISE EXCEPTION 'target_value_snapshot must equal TargetAssignment.target_value when created';
        END IF;

        IF target.employee_id IS NOT NULL AND target.employee_id <> NEW.employee_id THEN
            RAISE EXCEPTION 'Employee-level TargetAssignment must target the Contribution employee';
        END IF;

        IF target.department_id IS NOT NULL THEN
            SELECT department_id INTO target_department
            FROM employee
            WHERE id = NEW.employee_id;
            IF target.department_id <> target_department THEN
                RAISE EXCEPTION 'Department-level TargetAssignment must belong to the Contribution employee current department';
            END IF;
        END IF;

        IF NEW.target_version_snapshot IS NULL THEN
            RAISE EXCEPTION 'KPI-level Contribution requires target_version_snapshot';
        END IF;

        IF NEW.target_value_snapshot IS NULL THEN
            RAISE EXCEPTION 'KPI-level Contribution requires target_value_snapshot';
        END IF;

        IF EXISTS (
            SELECT 1 FROM kpi_definition k
            WHERE k.id = NEW.kpi_definition_id
              AND k.aggregation_type = 'RATIO'
        ) THEN
            IF NEW.actual_denominator IS NULL OR NEW.actual_denominator <= 0 THEN
                RAISE EXCEPTION 'RATIO KPI Contribution requires actual_denominator > 0';
            END IF;
        ELSIF NEW.actual_denominator IS NOT NULL THEN
            RAISE EXCEPTION 'Non-RATIO KPI Contribution must not have actual_denominator';
        END IF;

        IF NEW.weight_snapshot IS NOT NULL THEN
            RAISE EXCEPTION 'KPI-level Contribution must not store group weight_snapshot';
        END IF;
    ELSE
        IF NEW.parent_contribution_id IS NOT NULL THEN
            RAISE EXCEPTION 'Group-level Contribution cannot have a parent';
        END IF;

        SELECT evaluation_profile_id INTO profile_item_profile
        FROM evaluation_profile_item
        WHERE id = NEW.evaluation_profile_item_id;

        IF profile_item_profile IS NULL THEN
            RAISE EXCEPTION 'EvaluationProfileItem % not found', NEW.evaluation_profile_item_id;
        END IF;

        IF profile_item_profile <> pe.evaluation_profile_id THEN
            RAISE EXCEPTION 'Group-level Contribution item must belong to PerformanceEvaluation profile';
        END IF;

        IF NEW.target_assignment_id IS NOT NULL
           OR NEW.target_value_snapshot IS NOT NULL
           OR NEW.actual_value IS NOT NULL THEN
            RAISE EXCEPTION 'Group-level Contribution must not store target/actual fields';
        END IF;

        IF NEW.weight_snapshot IS NULL THEN
            RAISE EXCEPTION 'Group-level Contribution requires weight_snapshot';
        END IF;

        IF NEW.weight_snapshot <> (
            SELECT weight_percent
            FROM evaluation_profile_item
            WHERE id = NEW.evaluation_profile_item_id
        ) THEN
            RAISE EXCEPTION 'Group-level Contribution weight_snapshot must equal EvaluationProfileItem.weight_percent at creation';
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_validate_performance_contribution
BEFORE INSERT OR UPDATE ON performance_contribution
FOR EACH ROW EXECUTE FUNCTION validate_performance_contribution();

-- ActualProgress <-> Contribution is an N:1 relationship; source rows may be
-- linked later when a PerformanceContribution is calculated.

-- --------------------------------------------------------------------------
-- Automatically persist cap semantics in Contribution is application-owned;
-- DB only guarantees the configured cap and basic numeric ranges.
-- --------------------------------------------------------------------------
ALTER TABLE performance_contribution
    ADD CONSTRAINT ck_performance_contribution_achievement_raw
        CHECK (achievement_percent_raw IS NULL OR achievement_percent_raw >= 0),
    ADD CONSTRAINT ck_performance_contribution_achievement_capped
        CHECK (achievement_percent_capped IS NULL OR achievement_percent_capped >= 0),
    ADD CONSTRAINT ck_performance_contribution_weight_snapshot
        CHECK (weight_snapshot IS NULL OR (weight_snapshot > 0 AND weight_snapshot <= 100));

-- Raw achievement is the uncapped score; capped achievement cannot exceed raw
-- achievement or the KPI's configured cap when a cap is present.
ALTER TABLE performance_contribution
    ADD CONSTRAINT ck_performance_contribution_capped_not_above_raw
        CHECK (
            achievement_percent_raw IS NULL
            OR achievement_percent_capped IS NULL
            OR achievement_percent_capped <= achievement_percent_raw
        );

CREATE OR REPLACE FUNCTION validate_performance_contribution_cap()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    cap NUMERIC(10,4);
BEGIN
    IF NEW.kpi_definition_id IS NOT NULL AND NEW.achievement_percent_capped IS NOT NULL THEN
        SELECT max_achievement_percent
        INTO cap
        FROM kpi_definition
        WHERE id = NEW.kpi_definition_id;

        IF cap IS NOT NULL AND NEW.achievement_percent_capped > cap THEN
            RAISE EXCEPTION 'achievement_percent_capped % exceeds KPI cap %',
                NEW.achievement_percent_capped, cap;
        END IF;
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_validate_performance_contribution_cap
BEFORE INSERT OR UPDATE ON performance_contribution
FOR EACH ROW EXECUTE FUNCTION validate_performance_contribution_cap();

-- ============================================================================
-- Deferred integrity for all target-parent changes: trigger already defined.
-- ============================================================================

