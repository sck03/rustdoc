//! Frozen responsibility is historical data; current participation rechecks its source.
use crate::foundation::authority;

pub(crate) fn condition(
    pg: bool,
    record: &str,
    origin: &str,
    person: &str,
    date: Option<chrono::NaiveDate>,
) -> String {
    // Reference/handover audits deliberately include expired assignments.
    let Some(date) = date else {
        return "TRUE".into();
    };
    // NaiveDate supplies a canonical date, never caller-provided SQL text.
    let day = format!("'{date}'");
    let field = |name| format!("{origin}->>'{name}'");
    let source = field("source");
    let key = field("appointmentId");
    let unit = field("unitCode");
    let id = format!("CAST({} AS BIGINT)", field("sourceId"));
    let department = format!("CAST({} AS BIGINT)", field("departmentId"));
    let kind = format!(
        "CASE {record}.kind WHEN 'bookings' THEN 'room' WHEN 'supply-requests' THEN 'supply' ELSE substr({record}.kind,4) END"
    );
    let project = format!("CAST(NULLIF({record}.body->>'projectId','') AS BIGINT)");
    let position = authority::placement("Position", "m", "m.unit_id", &id, &day)
        .expect("known position source");
    let project_leader = authority::placement("ProjectLeader", "m", "m.unit_id", &id, &day)
        .expect("known project source");
    let responsibility = authority::responsibility(
        pg,
        ("duty_rule", "role_assignment", "m"),
        &project,
        &kind,
        &day,
    );
    let duty_kinds = authority::contains(pg, "d.workflow_kinds", &kind);
    let department_duty = format!(
        "EXISTS(SELECT 1 FROM organization_duties d JOIN organization_departments dep ON dep.id=d.organization_id JOIN organization_labels label ON label.id=dep.id WHERE dep.id={department} AND dep.unit_id=m.unit_id AND label.is_active=1 AND d.appointment_id=m.id AND (d.duty_kind='Primary' OR {source}='DepartmentLeaders' AND {duty_kinds}))"
    );
    let company_duty = format!(
        "EXISTS(SELECT 1 FROM organization_duties d WHERE d.organization_id=m.unit_id AND d.appointment_id=m.id AND (d.duty_kind='Primary' OR {source}='CompanyLeaders' AND {duty_kinds}))"
    );
    let subject = field("subjectPersonId");
    let subject_key = field("subjectAppointmentId");
    let subject_current = authority::appointment("subject", &day);
    let reporting = format!(
        "({subject} IS NULL OR EXISTS(SELECT 1 FROM reporting_relations relation JOIN personnel_memberships subject ON subject.id=relation.appointment_id JOIN persons subject_person ON subject_person.id=subject.person_id WHERE subject.id={subject_key} AND subject.person_id=CAST({subject} AS BIGINT) AND subject.unit_id=m.unit_id AND subject_person.status IN ('Active','Probation') AND {subject_current} AND relation.manager_appointment_id=m.id AND relation.relation_type=CASE {source} WHEN 'LineManager' THEN 'Line' ELSE 'Functional' END))"
    );
    let role = format!(
        "CASE {source} WHEN 'Named' THEN TRUE WHEN 'Handlers' THEN TRUE WHEN 'Requester' THEN TRUE WHEN 'Position' THEN ({position}) WHEN 'ProjectLeader' THEN ({project_leader}) WHEN 'Responsibility' THEN EXISTS(SELECT 1 FROM responsibilities duty_rule JOIN responsibility_assignments role_assignment ON role_assignment.responsibility_id=duty_rule.id WHERE duty_rule.id={id} AND duty_rule.unit_id=m.unit_id AND role_assignment.appointment_id=m.id AND {responsibility}) WHEN 'DepartmentManager' THEN {department_duty} WHEN 'DepartmentLeaders' THEN {department_duty} WHEN 'CompanyExecutive' THEN {company_duty} WHEN 'CompanyLeaders' THEN {company_duty} WHEN 'LineManager' THEN {reporting} WHEN 'FunctionalManagers' THEN {reporting} ELSE FALSE END"
    );
    let current = authority::appointment("m", &day);
    format!(
        "({source} IS NULL OR (COALESCE({key},'')='' AND {source} IN ('Named','Handlers','Requester')) OR EXISTS(SELECT 1 FROM personnel_memberships m JOIN persons person ON person.id=m.person_id JOIN organization_labels unit_label ON unit_label.id=m.unit_id JOIN organization_labels department_label ON department_label.id=m.department_id WHERE m.id={key} AND m.person_id={person} AND m.company={unit} AND person.status IN ('Active','Probation') AND unit_label.is_active=1 AND department_label.is_active=1 AND {current} AND ({role})))"
    )
}
