-- DEV ONLY mock users, roles and scoped grants. Needs dev-seed.sql first (scope tree by name).
-- Usernames match the Keycloak realm users (password = username); first sign-in links the row by username.
--   docker exec -i -e PGPASSWORD=<pwd> Sergin.Database psql -U postgres -d Sergin_DB -v ON_ERROR_STOP=1 < docker-compose/.files/seed/dev-seed-users.sql
-- Idempotent: exits when user admin.root exists.
SELECT count(*) > 0 AS seeded FROM ua.users WHERE user_name = 'admin.root' \gset
\if :seeded
\echo users already seeded, skipping
\quit
\endif
BEGIN;
INSERT INTO ua.users(id,user_name,is_active,email,first_name,last_name) VALUES
 (gen_random_uuid(),'admin.root',true,'admin.root@sergin.local','Root','Admin'),
 (gen_random_uuid(),'north.manager',true,'north.manager@sergin.local','North','Manager'),
 (gen_random_uuid(),'tabriz.operator',true,'tabriz.operator@sergin.local','Tabriz','Operator'),
 (gen_random_uuid(),'south.viewer',true,'south.viewer@sergin.local','South','Viewer'),
 (gen_random_uuid(),'multi.auditor',true,'multi.auditor@sergin.local','Multi','Auditor'),
 (gen_random_uuid(),'expired.user',true,'expired.user@sergin.local','Expired','User'),
 (gen_random_uuid(),'inactive.user',false,'inactive.user@sergin.local','Inactive','User');

INSERT INTO ua.roles(id,name,owner_scope_id,is_inheritable,row_version,created_at_utc,created_by) VALUES
 (gen_random_uuid(),'region-manager','01920000-0000-7000-8000-0000000000b1',true,gen_random_uuid(),now(),'01920000-0000-7000-8000-00000000000f'),
 (gen_random_uuid(),'operator','01920000-0000-7000-8000-0000000000b1',true,gen_random_uuid(),now(),'01920000-0000-7000-8000-00000000000f');
INSERT INTO ua.role_permissions(role_id,permission,created_at_utc,created_by)
SELECT r.id, p, now(), '01920000-0000-7000-8000-00000000000f'
FROM ua.roles r, unnest(CASE r.name
  WHEN 'region-manager' THEN ARRAY['permission.dm.devices.read','permission.dm.devices.update','permission.dm.devices.delete','permission.dm.manufacturers.read','permission.ua.users.read']
  ELSE ARRAY['permission.dm.devices.read','permission.dm.devices.update','permission.dm.manufacturers.read'] END) p
WHERE r.name IN ('region-manager','operator');

INSERT INTO ua.role_assignments(id,role_id,scope_id,include_subtree,valid_until_utc,user_id)
SELECT gen_random_uuid(), r.id, s.id, a.sub, a.until, u.id
FROM (VALUES
 ('admin.root',      'administrator',  'Root',        true,  NULL::timestamptz),
 ('north.manager',   'region-manager', 'North Region',true,  NULL),
 ('tabriz.operator', 'operator',       'Tabriz',      false, NULL),
 ('south.viewer',    'viewer',         'South Region',true,  NULL),
 ('multi.auditor',   'viewer',         'Shiraz',      false, NULL),
 ('multi.auditor',   'viewer',         'Mashhad',     false, NULL),
 ('expired.user',    'viewer',         'Root',        true,  now() - interval '7 days'),
 ('inactive.user',   'viewer',         'Root',        true,  NULL)
) a(usr,role,scope,sub,until)
JOIN ua.users u ON u.user_name=a.usr
JOIN ua.roles r ON r.name=a.role AND r.deleted_at_utc IS NULL
JOIN ua.scopes s ON s.name=a.scope AND s.deleted_at_utc IS NULL;
COMMIT;
