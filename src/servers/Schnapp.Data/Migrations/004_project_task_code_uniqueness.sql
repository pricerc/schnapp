-- Project.Code and Task.Code should only be unique within their parent scope,
-- not globally, so different customers/projects can reuse the same code.

ALTER TABLE project DROP CONSTRAINT uq_project_code;
ALTER TABLE project ADD CONSTRAINT uq_project_customer_code UNIQUE (customer_id, code);

ALTER TABLE task DROP CONSTRAINT uq_task_code;
ALTER TABLE task ADD CONSTRAINT uq_task_customer_project_code UNIQUE (customer_id, project_id, code);
