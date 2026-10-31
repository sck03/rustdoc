CREATE TABLE positions (
 id BIGSERIAL PRIMARY KEY,unit_id BIGINT NOT NULL REFERENCES organization_units(id),
 department_id BIGINT REFERENCES organization_departments(id),code TEXT NOT NULL,name TEXT NOT NULL,
 is_active BIGINT NOT NULL CHECK(is_active IN(0,1)),version BIGINT NOT NULL CHECK(version>0),
 UNIQUE(unit_id,code),UNIQUE(id,unit_id),FOREIGN KEY(department_id,unit_id) REFERENCES organization_departments(id,unit_id)
);
CREATE UNIQUE INDEX positions_name ON positions(unit_id,COALESCE(department_id,0),name);
ALTER TABLE personnel_memberships ADD COLUMN position_id BIGINT REFERENCES positions(id);
CREATE TABLE employee_identities (
 id BIGSERIAL PRIMARY KEY,person_id BIGINT NOT NULL REFERENCES persons(id) ON DELETE CASCADE,
 unit_id BIGINT NOT NULL REFERENCES organization_units(id),employee_number TEXT NOT NULL,
 UNIQUE(person_id,unit_id),UNIQUE(unit_id,employee_number),UNIQUE(id,unit_id)
);
CREATE TABLE employee_number_history (
 identity_id BIGINT NOT NULL REFERENCES employee_identities(id) ON DELETE CASCADE,
 unit_id BIGINT NOT NULL REFERENCES organization_units(id),employee_number TEXT NOT NULL,
 PRIMARY KEY(unit_id,employee_number),FOREIGN KEY(identity_id,unit_id) REFERENCES employee_identities(id,unit_id)
);
CREATE TABLE employments (
 id BIGSERIAL PRIMARY KEY,identity_id BIGINT NOT NULL REFERENCES employee_identities(id) ON DELETE CASCADE,
 unit_id BIGINT NOT NULL REFERENCES organization_units(id),starts_on TEXT NOT NULL,ends_on TEXT,
 stage TEXT NOT NULL CHECK(stage IN('Probation','Regular','Departed')),version BIGINT NOT NULL CHECK(version>0),
 CHECK(ends_on IS NULL OR ends_on>starts_on),UNIQUE(identity_id,starts_on),
 FOREIGN KEY(identity_id,unit_id) REFERENCES employee_identities(id,unit_id)
);
CREATE INDEX employments_period ON employments(identity_id,starts_on,ends_on);
CREATE TABLE responsibilities (
 id BIGSERIAL PRIMARY KEY,unit_id BIGINT NOT NULL REFERENCES organization_units(id),
 department_id BIGINT REFERENCES organization_departments(id),code TEXT NOT NULL,name TEXT NOT NULL,
 workflow_kinds JSONB NOT NULL,is_active BIGINT NOT NULL CHECK(is_active IN(0,1)),version BIGINT NOT NULL CHECK(version>0),
 UNIQUE(unit_id,code),UNIQUE(id,unit_id),FOREIGN KEY(department_id,unit_id) REFERENCES organization_departments(id,unit_id)
);
CREATE TABLE project_scopes (
 id BIGSERIAL PRIMARY KEY,unit_id BIGINT NOT NULL REFERENCES organization_units(id),
 code TEXT NOT NULL,name TEXT NOT NULL,owner_appointment_id TEXT NOT NULL REFERENCES personnel_memberships(id),
 is_active BIGINT NOT NULL CHECK(is_active IN(0,1)),version BIGINT NOT NULL CHECK(version>0),
 UNIQUE(unit_id,code),UNIQUE(id,unit_id)
);
CREATE TABLE responsibility_assignments (
 id BIGSERIAL PRIMARY KEY,responsibility_id BIGINT NOT NULL REFERENCES responsibilities(id),
 unit_id BIGINT NOT NULL,appointment_id TEXT NOT NULL REFERENCES personnel_memberships(id),
 project_id BIGINT REFERENCES project_scopes(id),starts_on TEXT NOT NULL,ends_on TEXT,
 CHECK(ends_on IS NULL OR ends_on>starts_on),UNIQUE(responsibility_id,appointment_id,project_id,starts_on),
 FOREIGN KEY(responsibility_id,unit_id) REFERENCES responsibilities(id,unit_id),
 FOREIGN KEY(project_id,unit_id) REFERENCES project_scopes(id,unit_id)
);
CREATE INDEX responsibility_assignments_period ON responsibility_assignments(responsibility_id,starts_on,ends_on);
CREATE TABLE project_memberships (
 id BIGSERIAL PRIMARY KEY,project_id BIGINT NOT NULL REFERENCES project_scopes(id),
 unit_id BIGINT NOT NULL,appointment_id TEXT NOT NULL REFERENCES personnel_memberships(id),
 role TEXT NOT NULL CHECK(role IN('Member','Leader')),starts_on TEXT NOT NULL,ends_on TEXT,
 CHECK(ends_on IS NULL OR ends_on>starts_on),UNIQUE(project_id,appointment_id,starts_on),
 FOREIGN KEY(project_id,unit_id) REFERENCES project_scopes(id,unit_id)
);
CREATE INDEX project_memberships_period ON project_memberships(project_id,starts_on,ends_on);
CREATE FUNCTION validate_foundation_relation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_TABLE_NAME='employments' THEN
  PERFORM id FROM employee_identities WHERE id=NEW.identity_id FOR UPDATE;
  IF EXISTS(SELECT 1 FROM employments e WHERE e.id<>NEW.id AND e.identity_id=NEW.identity_id AND e.starts_on<COALESCE(NEW.ends_on,'9999-12-31') AND NEW.starts_on<COALESCE(e.ends_on,'9999-12-31')) THEN
   RAISE EXCEPTION 'Employment periods overlap' USING ERRCODE='23514';
  END IF;
 ELSIF TG_TABLE_NAME='personnel_memberships' THEN
  IF NEW.position_id IS NOT NULL AND NOT EXISTS(SELECT 1 FROM positions p WHERE p.id=NEW.position_id AND p.unit_id=NEW.unit_id AND (p.department_id IS NULL OR p.department_id=NEW.department_id)) THEN
   RAISE EXCEPTION 'Appointment position crosses unit or department' USING ERRCODE='23514';
  END IF;
 ELSE
  IF NOT EXISTS(SELECT 1 FROM personnel_memberships m WHERE m.id=NEW.appointment_id AND m.unit_id=NEW.unit_id) THEN
   RAISE EXCEPTION 'Foundation appointment crosses unit' USING ERRCODE='23514';
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER employment_period_guard BEFORE INSERT OR UPDATE ON employments FOR EACH ROW EXECUTE FUNCTION validate_foundation_relation();
CREATE TRIGGER membership_position_guard BEFORE INSERT OR UPDATE ON personnel_memberships FOR EACH ROW EXECUTE FUNCTION validate_foundation_relation();
CREATE TRIGGER responsibility_appointment_guard BEFORE INSERT OR UPDATE ON responsibility_assignments FOR EACH ROW EXECUTE FUNCTION validate_foundation_relation();
CREATE TRIGGER project_appointment_guard BEFORE INSERT OR UPDATE ON project_memberships FOR EACH ROW EXECUTE FUNCTION validate_foundation_relation();
