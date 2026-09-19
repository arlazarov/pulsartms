-- Clears operational data and proves the identity boundary is untouched.
-- Run only with writers stopped, after a verified backup, and only against the
-- schema named below: the table list is explicit so an unfamiliar table
-- rejects the run instead of being truncated silently. ResetInventoryTests
-- keeps that list matching the model; update both together.
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
    'BorderCrew',
    'BorderCrossings',
    'BorderEquipment',
    'BorderSaveReceipts',
    'BorderShipments',
    'Customers',
    'CustomsCommodities',
    'DispatchActivityEntries',
    'DispatchActivityThreads',
    'DispatchBaseRoutes',
    'DispatchDeadheads',
    'DispatchDocuments',
    'DispatchEtaForecasts',
    'DispatchNumberCounters',
    'DispatchRates',
    'DispatchRouteChoices',
    'DispatchRoutePlans',
    'DispatchRoutePreviews',
    'DispatchSettings',
    'DispatchSourceLinks',
    'DispatchStopCompletionEvents',
    'DispatchStops',
    'DispatchSwitchOperations',
    'DispatchWorkspaceRevisions',
    'DispatchWorkspaces',
    'DriverHosReadings',
    'Dispatches',
    'Drivers',
    'ExecutionActionReceipts',
    'ExecutionLegRevisions',
    'ExecutionLegStops',
    'ExecutionLegs',
    'ExecutionPlanningChanges',
    'ExecutionSourceReceipts',
    'ExpenseAttributionEvents',
    'ExpenseAttributions',
    'Expenses',
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
    'PlanningInputRevisions',
    'PlanningRefreshRequests',
    'RouteRecalculationAttempts',
    'RoutingApiCalls',
    'ShipmentSaveReceipts',
    'Shipments',
    'SourceRoadRequests',
    'SwitchParticipants',
    'SynchronizationCheckpoints',
    'TrailerCustodyIntervals',
    'TruckLocationReadings',
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
  IF (SELECT count(*) FROM "__EFMigrationsHistory") <> 47
    OR (SELECT max("MigrationId") FROM "__EFMigrationsHistory")
      IS DISTINCT FROM '20260919162343_StoreTruckPositions' THEN
    RAISE EXCEPTION 'Reset requires the schema this inventory was reviewed for';
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
