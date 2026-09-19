-- Run only during the approved nonrolling cutover, after verified backup.
-- Connection settings acknowledge the target, backup and stopped writers.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
DO $$
DECLARE
  protected text[] := ARRAY[
    'AspNetRoleClaims',
    'AspNetRoles',
    'AspNetUserClaims',
    'AspNetUserLogins',
    'AspNetUserRoles',
    'AspNetUserTokens',
    'AspNetUsers',
    'DataProtectionKeys',
    'IntegrationCredentialSettings',
    'Users',
    '__EFMigrationsHistory'
  ];
  operational text[] := ARRAY[
    'Customers',
    'DispatchActivityEntries',
    'DispatchActivityThreads',
    'DispatchBaseRoutes',
    'DispatchDeadheads',
    'DispatchDocuments',
    'DispatchEtaForecasts',
    'DispatchRates',
    'DispatchRouteChoices',
    'DispatchRoutePlans',
    'DispatchRoutePreviews',
    'DispatchSettings',
    'DispatchStopCompletionEvents',
    'DispatchStops',
    'DispatchSwitchOperations',
    'DispatchWorkspaceRevisions',
    'DispatchWorkspaces',
    'Dispatches',
    'Drivers',
    'ExecutionActionReceipts',
    'ExecutionLegs',
    'ExecutionPlanningChanges',
    'ExecutionSourceReceipts',
    'ExecutionVisits',
    'FleetPlanningSettings',
    'FuelDiscounts',
    'FuelImportSources',
    'FuelStations',
    'FuelTransactions',
    'IftaTaxRates',
    'LoadExecutionLegs',
    'MileageAllocationPolicies',
    'MileageCaptureGaps',
    'MovementAllocationEvents',
    'MovementDistanceEvidence',
    'Movements',
    'OdometerCaptureCheckpoints',
    'OdometerIntervals',
    'OdometerPositions',
    'RouteRecalculationAttempts',
    'RoutingApiCalls',
    'SwitchParticipants',
    'SynchronizationCheckpoints',
    'TrailerCustodyIntervals',
    'Trailers',
    'Trips',
    'TruckFuelPlans',
    'TruckPlanningProfiles',
    'Trucks'
  ];
  expected text[];
  actual text[];
  tables_sql text;
  table_name text;
  digest text;
  before_reset jsonb := '{}'::jsonb;
  after_reset jsonb := '{}'::jsonb;
BEGIN
  IF current_setting('pulsr.reset_database', true)
      IS DISTINCT FROM current_database()
    OR current_setting('pulsr.reset_ack', true)
      IS DISTINCT FROM '20260917055902_RebuildExecutionStorage'
    OR current_setting('pulsr.reset_writers_stopped', true)
      IS DISTINCT FROM 'true'
    OR current_setting('pulsr.reset_backup_verified', true)
      IS DISTINCT FROM 'true' THEN
    RAISE EXCEPTION
      'Explicit target, backup and stopped-writer checks required';
  END IF;
  IF EXISTS (
    SELECT 1 FROM pg_stat_activity
    WHERE datname = current_database() AND pid <> pg_backend_pid()
      AND backend_type = 'client backend'
  ) THEN
    RAISE EXCEPTION 'Other database clients must disconnect before reset';
  END IF;
  SELECT array_agg(name ORDER BY name) INTO expected
    FROM unnest(protected || operational) AS names(name);
  SELECT array_agg(tablename::text ORDER BY tablename) INTO actual
    FROM pg_tables WHERE schemaname = 'public';
  IF actual IS DISTINCT FROM expected THEN
    RAISE EXCEPTION 'Database tables differ from the reviewed reset inventory';
  END IF;
  SELECT string_agg(format('public.%I', name), ', ' ORDER BY name)
    INTO tables_sql FROM unnest(expected) AS names(name);
  EXECUTE 'LOCK TABLE ' || tables_sql || ' IN ACCESS EXCLUSIVE MODE NOWAIT';
  IF (SELECT count(*) FROM "__EFMigrationsHistory") <> 39
    OR (SELECT max("MigrationId") FROM "__EFMigrationsHistory")
      IS DISTINCT FROM '20260914214701_AddStopCorrections' THEN
    RAISE EXCEPTION 'Reset requires the reviewed pre-rebuild schema';
  END IF;
  FOREACH table_name IN ARRAY protected LOOP
    EXECUTE format(
      'SELECT md5(COALESCE(jsonb_agg(to_jsonb(t) '
      || 'ORDER BY to_jsonb(t)::text), ''[]''::jsonb)::text) FROM public.%I t',
      table_name
    ) INTO digest;
    before_reset := before_reset || jsonb_build_object(table_name, digest);
  END LOOP;
  SELECT string_agg(format('public.%I', name), ', ' ORDER BY name)
    INTO tables_sql FROM unnest(operational) AS names(name);
  EXECUTE 'TRUNCATE TABLE ' || tables_sql;
  FOREACH table_name IN ARRAY protected LOOP
    EXECUTE format(
      'SELECT md5(COALESCE(jsonb_agg(to_jsonb(t) '
      || 'ORDER BY to_jsonb(t)::text), ''[]''::jsonb)::text) FROM public.%I t',
      table_name
    ) INTO digest;
    after_reset := after_reset || jsonb_build_object(table_name, digest);
  END LOOP;
  IF before_reset IS DISTINCT FROM after_reset THEN
    RAISE EXCEPTION 'Reset changed protected identity or configuration';
  END IF;
END $$;
COMMIT;
