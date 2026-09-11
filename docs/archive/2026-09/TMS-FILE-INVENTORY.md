# AMFTMS file inventory — September 5, 2026

Historical inventory: 261 Client/Server files excluding bin/obj, plus root configuration and scripts. Paths reflect that date and may have moved. This is not a claim that every scenario was tested. See the [historical review](TMS-REVIEW-2026-09-05.md).

For the maintained structure use [server boundaries](../../ARCHITECTURE.md),
[styles](../../architecture/styles.md), [JavaScript](../../architecture/javascript.md) and
[test ownership](../../testing.md). Do not use this dated path list as an import map.

| File | Role |
|---|---|
| [AMFTMS.slnx](../../../AMFTMS.slnx) | Configuration / build / deployment |
| [cloudbuild.yaml](../../../cloudbuild.yaml) | Configuration / build / deployment |
| [deploy-client.sh](../../../deploy-client.sh) | Development / deployment script |
| [deploy-server.sh](../../../deploy-server.sh) | Development / deployment script |
| [firebase.json](../../../firebase.json) | Configuration / build / deployment |
| [migrate.sh](../../../migrate.sh) | Development / deployment script |
| [Client/App.razor](../../../Client/App.razor) | UI markup / routing |
| [Client/Client.csproj](../../../Client/Client.csproj) | Configuration / build / deployment |
| [Client/Components/DataTable/DataTable.razor](../../../Client/Components/DataTable/DataTable.razor) | UI markup / routing |
| [Client/Components/DataTable/DataTable.razor.cs](../../../Client/Components/DataTable/DataTable.razor.cs) | UI logic |
| [Client/Components/DataTable/DataTablePagination.razor](../../../Client/Components/DataTable/DataTablePagination.razor) | UI markup / routing |
| [Client/Components/DataTable/DataTablePagination.razor.cs](../../../Client/Components/DataTable/DataTablePagination.razor.cs) | UI logic |
| [Client/Components/Form/Form.razor](../../../Client/Components/Form/Form.razor) | UI markup / routing |
| [Client/Components/Form/Form.razor.cs](../../../Client/Components/Form/Form.razor.cs) | UI logic |
| [Client/Components/Form/FormField.razor](../../../Client/Components/Form/FormField.razor) | UI markup / routing |
| [Client/Components/Form/FormField.razor.cs](../../../Client/Components/Form/FormField.razor.cs) | UI logic |
| [Client/Components/Popup/Popup.razor](../../../Client/Components/Popup/Popup.razor) | UI markup / routing |
| [Client/Components/Popup/Popup.razor.cs](../../../Client/Components/Popup/Popup.razor.cs) | UI logic |
| [Client/Layout/AuthLayout.razor](../../../Client/Layout/AuthLayout.razor) | UI markup / routing |
| [Client/Layout/MainLayout.razor](../../../Client/Layout/MainLayout.razor) | UI markup / routing |
| [Client/Layout/Sidebar.razor](../../../Client/Layout/Sidebar.razor) | UI markup / routing |
| [Client/Layout/Sidebar.razor.cs](../../../Client/Layout/Sidebar.razor.cs) | UI logic |
| [Client/Models/Auth/AuthResponse.cs](../../../Client/Models/Auth/AuthResponse.cs) | Contract / DTO |
| [Client/Models/Auth/LoginRequest.cs](../../../Client/Models/Auth/LoginRequest.cs) | Contract / DTO |
| [Client/Models/DTO/PaginatedListDTO.cs](../../../Client/Models/DTO/PaginatedListDTO.cs) | Contract / DTO |
| [Client/Models/DTO/RequestResponseDTO.cs](../../../Client/Models/DTO/RequestResponseDTO.cs) | Contract / DTO |
| [Client/Models/DTO/UserDTO.cs](../../../Client/Models/DTO/UserDTO.cs) | Contract / DTO |
| [Client/Pages/Auth/Login.razor](../../../Client/Pages/Auth/Login.razor) | UI markup / routing |
| [Client/Pages/Auth/Login.razor.cs](../../../Client/Pages/Auth/Login.razor.cs) | UI logic |
| [Client/Pages/Base/FormPage.cs](../../../Client/Pages/Base/FormPage.cs) | Supporting logic / documentation / startup |
| [Client/Pages/FleetMap/FleetDtos.cs](../../../Client/Pages/FleetMap/FleetDtos.cs) | Contract / DTO |
| [Client/Pages/FleetMap/FleetMap.razor](../../../Client/Pages/FleetMap/FleetMap.razor) | UI markup / routing |
| [Client/Pages/FleetMap/FleetMap.razor.cs](../../../Client/Pages/FleetMap/FleetMap.razor.cs) | UI logic |
| [Client/Pages/Home/Home.razor](../../../Client/Pages/Home/Home.razor) | UI markup / routing |
| [Client/Pages/Home/Home.razor.cs](../../../Client/Pages/Home/Home.razor.cs) | UI logic |
| [Client/Pages/Users/AddUser.razor](../../../Client/Pages/Users/AddUser.razor) | UI markup / routing |
| [Client/Pages/Users/AddUser.razor.cs](../../../Client/Pages/Users/AddUser.razor.cs) | UI logic |
| [Client/Pages/Users/EditUser.razor](../../../Client/Pages/Users/EditUser.razor) | UI markup / routing |
| [Client/Pages/Users/EditUser.razor.cs](../../../Client/Pages/Users/EditUser.razor.cs) | UI logic |
| [Client/Pages/Users/Users.razor](../../../Client/Pages/Users/Users.razor) | UI markup / routing |
| [Client/Pages/Users/Users.razor.cs](../../../Client/Pages/Users/Users.razor.cs) | UI logic |
| [Client/Program.cs](../../../Client/Program.cs) | Supporting logic / documentation / startup |
| [Client/Properties/launchSettings.json](../../../Client/Properties/launchSettings.json) | Configuration / build / deployment |
| [Client/Services/ApiService.cs](../../../Client/Services/ApiService.cs) | Application / infrastructure service |
| [Client/Services/AppAuthenticationStateProvider.cs](../../../Client/Services/AppAuthenticationStateProvider.cs) | Application / infrastructure service |
| [Client/Services/AuthHeaderHandler.cs](../../../Client/Services/AuthHeaderHandler.cs) | Application / infrastructure service |
| [Client/Services/AuthService.cs](../../../Client/Services/AuthService.cs) | Application / infrastructure service |
| [Client/Services/RefreshLoop.cs](../../../Client/Services/RefreshLoop.cs) | Application / infrastructure service |
| [Client/Services/TokenStorageService.cs](../../../Client/Services/TokenStorageService.cs) | Application / infrastructure service |
| [Client/Shared/RedirectToLogin.razor](../../../Client/Shared/RedirectToLogin.razor) | UI markup / routing |
| [Client/Styles/base/_colors.scss](../../../Client/Styles/base/_colors.scss) | Source styles |
| [Client/Styles/base/_functions.scss](../../../Client/Styles/base/_functions.scss) | Source styles |
| [Client/Styles/base/_mixins.scss](../../../Client/Styles/base/_mixins.scss) | Source styles |
| [Client/Styles/base/_reset.scss](../Client/Styles/base/_reset.scss) | Source styles |
| [Client/Styles/base/_typography.scss](../Client/Styles/base/_typography.scss) | Source styles |
| [Client/Styles/base/_variables.scss](../../../Client/Styles/base/_variables.scss) | Source styles |
| [Client/Styles/components/_buttons.scss](../../../Client/Styles/components/_buttons.scss) | Source styles |
| [Client/Styles/components/_data-table-pagination.scss](../../../Client/Styles/components/_data-table-pagination.scss) | Source styles |
| [Client/Styles/components/_data-table.scss](../../../Client/Styles/components/_data-table.scss) | Source styles |
| [Client/Styles/components/_delete-confirm.scss](../Client/Styles/components/_delete-confirm.scss) | Source styles |
| [Client/Styles/components/_form-fields.scss](../../../Client/Styles/components/_form-fields.scss) | Source styles |
| [Client/Styles/components/_form.scss](../../../Client/Styles/components/_form.scss) | Source styles |
| [Client/Styles/components/_index.scss](../../../Client/Styles/components/_index.scss) | Source styles |
| [Client/Styles/components/_popup.scss](../../../Client/Styles/components/_popup.scss) | Source styles |
| [Client/Styles/components/fleet-map/_index.scss](../Client/Styles/components/fleet-map/_index.scss) | Source styles |
| [Client/Styles/components/fleet-map/_station-marker.scss](../Client/Styles/components/fleet-map/_station-marker.scss) | Source styles |
| [Client/Styles/components/fleet-map/_station-popup.scss](../Client/Styles/components/fleet-map/_station-popup.scss) | Source styles |
| [Client/Styles/components/fleet-map/_truck-marker.scss](../Client/Styles/components/fleet-map/_truck-marker.scss) | Source styles |
| [Client/Styles/components/fleet-map/_truck-popup.scss](../Client/Styles/components/fleet-map/_truck-popup.scss) | Source styles |
| [Client/Styles/core/_root.scss](../Client/Styles/core/_root.scss) | Source styles |
| [Client/Styles/layouts/_auth-layout.scss](../../../Client/Styles/layouts/_auth-layout.scss) | Source styles |
| [Client/Styles/layouts/_index.scss](../../../Client/Styles/layouts/_index.scss) | Source styles |
| [Client/Styles/layouts/_main-layout.scss](../../../Client/Styles/layouts/_main-layout.scss) | Source styles |
| [Client/Styles/layouts/_sidebar.scss](../../../Client/Styles/layouts/_sidebar.scss) | Source styles |
| [Client/Styles/main.scss](../../../Client/Styles/main.scss) | Source styles |
| [Client/Styles/pages/_add-user-page.scss](../../../Client/Styles/pages/_add-user-page.scss) | Source styles |
| [Client/Styles/pages/_fleet-map-page.scss](../Client/Styles/pages/_fleet-map-page.scss) | Source styles |
| [Client/Styles/pages/_index.scss](../../../Client/Styles/pages/_index.scss) | Source styles |
| [Client/Styles/pages/_login-page.scss](../../../Client/Styles/pages/_login-page.scss) | Source styles |
| [Client/Styles/pages/_users-page.scss](../../../Client/Styles/pages/_users-page.scss) | Source styles |
| [Client/_Imports.razor](../../../Client/_Imports.razor) | UI markup / routing |
| [Client/watch-css.sh](../../../Client/watch-css.sh) | Development / deployment script |
| [Client/wwwroot/appsettings.json](../../../Client/wwwroot/appsettings.json) | Configuration / build / deployment |
| [Client/wwwroot/css/main.css](../../../Client/wwwroot/css/main.css) | Generated styles / source map |
| [Client/wwwroot/css/main.css.map](../../../Client/wwwroot/css/main.css.map) | Generated styles / source map |
| [Client/wwwroot/index.html](../../../Client/wwwroot/index.html) | Supporting logic / documentation / startup |
| [Client/wwwroot/js/fleetMap/fleetMap.js](../Client/wwwroot/js/fleetMap/fleetMap.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/fleetMap/googleMapsLoader.js](../Client/wwwroot/js/fleetMap/googleMapsLoader.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/fleetMap/stations/stationLayer.js](../Client/wwwroot/js/fleetMap/stations/stationLayer.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/fleetMap/stations/stationMarker.js](../Client/wwwroot/js/fleetMap/stations/stationMarker.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/fleetMap/stations/stationPopup.js](../Client/wwwroot/js/fleetMap/stations/stationPopup.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/fleetMap/stations/stationPrices.js](../Client/wwwroot/js/fleetMap/stations/stationPrices.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/fleetMap/trucks/truckLayer.js](../Client/wwwroot/js/fleetMap/trucks/truckLayer.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/fleetMap/trucks/truckMarker.js](../Client/wwwroot/js/fleetMap/trucks/truckMarker.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/fleetMap/trucks/truckOverlay.js](../Client/wwwroot/js/fleetMap/trucks/truckOverlay.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/fleetMap/trucks/truckPlayback.js](../Client/wwwroot/js/fleetMap/trucks/truckPlayback.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/fleetMap/trucks/truckPoints.js](../Client/wwwroot/js/fleetMap/trucks/truckPoints.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/fleetMap/trucks/truckPopup.js](../Client/wwwroot/js/fleetMap/trucks/truckPopup.js) | Map / DOM / browser interaction |
| [Client/wwwroot/js/popup.js](../Client/wwwroot/js/popup.js) | Map / DOM / browser interaction |
| [Server/.DS_Store](../../../Server/.DS_Store) | Supporting logic / documentation / startup |
| [Server/API/.DS_Store](../../../Server/API/.DS_Store) | Supporting logic / documentation / startup |
| [Server/API/API.csproj](../../../Server/API/API.csproj) | Configuration / build / deployment |
| [Server/API/API.http](../../../Server/API/API.http) | Supporting logic / documentation / startup |
| [Server/API/Controllers/AuthController.cs](../../../Server/API/Controllers/AuthController.cs) | HTTP endpoint |
| [Server/API/Controllers/BaseController.cs](../../../Server/API/Controllers/BaseController.cs) | HTTP endpoint |
| [Server/API/Controllers/DispatchController.cs](../../../Server/API/Controllers/DispatchController.cs) | HTTP endpoint |
| [Server/API/Controllers/FleetController.cs](../../../Server/API/Controllers/FleetController.cs) | HTTP endpoint |
| [Server/API/Controllers/FuelController.cs](../../../Server/API/Controllers/FuelController.cs) | HTTP endpoint |
| [Server/API/Controllers/UsersController.cs](../../../Server/API/Controllers/UsersController.cs) | HTTP endpoint |
| [Server/API/DependencyInjection.cs](../../../Server/API/DependencyInjection.cs) | Supporting logic / documentation / startup |
| [Server/API/Dockerfile](../../../Server/API/Dockerfile) | Configuration / build / deployment |
| [Server/API/Program.cs](../../../Server/API/Program.cs) | Supporting logic / documentation / startup |
| [Server/API/Properties/launchSettings.json](../../../Server/API/Properties/launchSettings.json) | Configuration / build / deployment |
| [Server/API/appsettings.Development.json](../../../Server/API/appsettings.Development.json) | Configuration / build / deployment |
| [Server/API/appsettings.json](../../../Server/API/appsettings.json) | Configuration / build / deployment |
| [Server/API/gmail-credentials.json](../../../Server/API/gmail-credentials.json) | Configuration / build / deployment |
| [Server/API/gmail-token/.DS_Store](../../../Server/API/gmail-token/.DS_Store) | Supporting logic / documentation / startup |
| [Server/API/gmail-token/Google.Apis.Auth.OAuth2.Responses.TokenResponse-amftms](../../../Server/API/gmail-token/Google.Apis.Auth.OAuth2.Responses.TokenResponse-amftms) | Supporting logic / documentation / startup |
| [Server/Application/Application.csproj](../../../Server/Application/Application.csproj) | Configuration / build / deployment |
| [Server/Application/Behaviors/ValidationBehavior.cs](../../../Server/Application/Behaviors/ValidationBehavior.cs) | Supporting logic / documentation / startup |
| [Server/Application/DependencyInjection.cs](../../../Server/Application/DependencyInjection.cs) | Supporting logic / documentation / startup |
| [Server/Application/Features/Auth/Commands/Login.cs](../../../Server/Application/Features/Auth/Commands/Login.cs) | Command / import / data transformation |
| [Server/Application/Features/Auth/Commands/Logout.cs](../../../Server/Application/Features/Auth/Commands/Logout.cs) | Command / import / data transformation |
| [Server/Application/Features/Auth/Commands/Refresh.cs](../../../Server/Application/Features/Auth/Commands/Refresh.cs) | Command / import / data transformation |
| [Server/Application/Features/Auth/Interfaces/IAuthService.cs](../../../Server/Application/Features/Auth/Interfaces/IAuthService.cs) | Application interface |
| [Server/Application/Features/Dispatch/Commands/SyncDispatche/CustomerMatcher.cs](../../../Server/Application/Features/Dispatch/Commands/SyncDispatche/CustomerMatcher.cs) | Command / import / data transformation |
| [Server/Application/Features/Dispatch/Commands/SyncDispatche/DispatchComparer.cs](../../../Server/Application/Features/Dispatch/Commands/SyncDispatche/DispatchComparer.cs) | Command / import / data transformation |
| [Server/Application/Features/Dispatch/Commands/SyncDispatche/DispatchMapper.cs](../../../Server/Application/Features/Dispatch/Commands/SyncDispatche/DispatchMapper.cs) | Command / import / data transformation |
| [Server/Application/Features/Dispatch/Commands/SyncDispatche/DriverMatcher.cs](../../../Server/Application/Features/Dispatch/Commands/SyncDispatche/DriverMatcher.cs) | Command / import / data transformation |
| [Server/Application/Features/Dispatch/Commands/SyncDispatche/SyncDispatche.cs](../../../Server/Application/Features/Dispatch/Commands/SyncDispatche/SyncDispatche.cs) | Command / import / data transformation |
| [Server/Application/Features/Dispatch/Interfaces/IDispatchProvider.cs](../../../Server/Application/Features/Dispatch/Interfaces/IDispatchProvider.cs) | Application interface |
| [Server/Application/Features/Dispatch/Models/DispatchResponse.cs](../../../Server/Application/Features/Dispatch/Models/DispatchResponse.cs) | Contract / DTO |
| [Server/Application/Features/Dispatch/Models/DispatchStopResponse.cs](../../../Server/Application/Features/Dispatch/Models/DispatchStopResponse.cs) | Contract / DTO |
| [Server/Application/Features/Dispatch/Models/ExternalDispatch.cs](../../../Server/Application/Features/Dispatch/Models/ExternalDispatch.cs) | Contract / DTO |
| [Server/Application/Features/Dispatch/Models/ExternalDispatchStop.cs](../../../Server/Application/Features/Dispatch/Models/ExternalDispatchStop.cs) | Contract / DTO |
| [Server/Application/Features/Dispatch/Queries/GetDispatche.cs](../../../Server/Application/Features/Dispatch/Queries/GetDispatche.cs) | Data query / projection / cache |
| [Server/Application/Features/Dispatch/Queries/GetTruckDispatch.cs](../../../Server/Application/Features/Dispatch/Queries/GetTruckDispatch.cs) | Data query / projection / cache |
| [Server/Application/Features/Fleet/Commands/SyncFleet/DriverSync.cs](../../../Server/Application/Features/Fleet/Commands/SyncFleet/DriverSync.cs) | Command / import / data transformation |
| [Server/Application/Features/Fleet/Commands/SyncFleet/FleetAssignmentSync.cs](../../../Server/Application/Features/Fleet/Commands/SyncFleet/FleetAssignmentSync.cs) | Command / import / data transformation |
| [Server/Application/Features/Fleet/Commands/SyncFleet/SyncFleet.cs](../../../Server/Application/Features/Fleet/Commands/SyncFleet/SyncFleet.cs) | Command / import / data transformation |
| [Server/Application/Features/Fleet/Commands/SyncFleet/TrailerSync.cs](../../../Server/Application/Features/Fleet/Commands/SyncFleet/TrailerSync.cs) | Command / import / data transformation |
| [Server/Application/Features/Fleet/Commands/SyncFleet/TruckSync.cs](../../../Server/Application/Features/Fleet/Commands/SyncFleet/TruckSync.cs) | Command / import / data transformation |
| [Server/Application/Features/Fleet/Interfaces/IFleetProvider.cs](../../../Server/Application/Features/Fleet/Interfaces/IFleetProvider.cs) | Application interface |
| [Server/Application/Features/Fleet/Interfaces/IFleetTelemetryProvider.cs](../../../Server/Application/Features/Fleet/Interfaces/IFleetTelemetryProvider.cs) | Application interface |
| [Server/Application/Features/Fleet/Models/ExternalDailyHosLog.cs](../../../Server/Application/Features/Fleet/Models/ExternalDailyHosLog.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Models/ExternalDriver.cs](../../../Server/Application/Features/Fleet/Models/ExternalDriver.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Models/ExternalFleetAssignment.cs](../../../Server/Application/Features/Fleet/Models/ExternalFleetAssignment.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Models/ExternalTrailer.cs](../../../Server/Application/Features/Fleet/Models/ExternalTrailer.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Models/ExternalTrailerAssignment.cs](../../../Server/Application/Features/Fleet/Models/ExternalTrailerAssignment.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Models/ExternalVehicle.cs](../../../Server/Application/Features/Fleet/Models/ExternalVehicle.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Models/FleetLocationsResponse.cs](../../../Server/Application/Features/Fleet/Models/FleetLocationsResponse.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Models/FleetTruckInfo.cs](../../../Server/Application/Features/Fleet/Models/FleetTruckInfo.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Models/TruckLocation.cs](../../../Server/Application/Features/Fleet/Models/TruckLocation.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Models/VehicleLocationPoint.cs](../../../Server/Application/Features/Fleet/Models/VehicleLocationPoint.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Models/VehicleLocationStream.cs](../../../Server/Application/Features/Fleet/Models/VehicleLocationStream.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Models/VehicleTelemetry.cs](../../../Server/Application/Features/Fleet/Models/VehicleTelemetry.cs) | Contract / DTO |
| [Server/Application/Features/Fleet/Queries/GetDrivers.cs](../../../Server/Application/Features/Fleet/Queries/GetDrivers.cs) | Data query / projection / cache |
| [Server/Application/Features/Fleet/Queries/GetFleetLocations/FleetCache.cs](../../../Server/Application/Features/Fleet/Queries/GetFleetLocations/FleetCache.cs) | Data query / projection / cache |
| [Server/Application/Features/Fleet/Queries/GetFleetLocations/FleetLocationStream.cs](../../../Server/Application/Features/Fleet/Queries/GetFleetLocations/FleetLocationStream.cs) | Data query / projection / cache |
| [Server/Application/Features/Fleet/Queries/GetFleetLocations/GetFleetLocations.cs](../../../Server/Application/Features/Fleet/Queries/GetFleetLocations/GetFleetLocations.cs) | Data query / projection / cache |
| [Server/Application/Features/Fleet/Queries/GetTrailers.cs](../../../Server/Application/Features/Fleet/Queries/GetTrailers.cs) | Data query / projection / cache |
| [Server/Application/Features/Fleet/Queries/GetTrucks.cs](../../../Server/Application/Features/Fleet/Queries/GetTrucks.cs) | Data query / projection / cache |
| [Server/Application/Features/Fuel/Commands/ImportFuelDiscounts/FuelDiscountSync.cs](../../../Server/Application/Features/Fuel/Commands/ImportFuelDiscounts/FuelDiscountSync.cs) | Command / import / data transformation |
| [Server/Application/Features/Fuel/Commands/ImportFuelDiscounts/FuelStationSync.cs](../../../Server/Application/Features/Fuel/Commands/ImportFuelDiscounts/FuelStationSync.cs) | Command / import / data transformation |
| [Server/Application/Features/Fuel/Commands/ImportFuelDiscounts/ImportFuelDiscounts.cs](../../../Server/Application/Features/Fuel/Commands/ImportFuelDiscounts/ImportFuelDiscounts.cs) | Command / import / data transformation |
| [Server/Application/Features/Fuel/Commands/StartGmailWatch.cs](../../../Server/Application/Features/Fuel/Commands/StartGmailWatch.cs) | Command / import / data transformation |
| [Server/Application/Features/Fuel/Commands/SyncIftaTaxRates/IftaTaxMatrixParser.cs](../../../Server/Application/Features/Fuel/Commands/SyncIftaTaxRates/IftaTaxMatrixParser.cs) | Command / import / data transformation |
| [Server/Application/Features/Fuel/Commands/SyncIftaTaxRates/IftaTaxRateSync.cs](../../../Server/Application/Features/Fuel/Commands/SyncIftaTaxRates/IftaTaxRateSync.cs) | Command / import / data transformation |
| [Server/Application/Features/Fuel/Commands/SyncIftaTaxRates/SyncIftaTaxRates.cs](../../../Server/Application/Features/Fuel/Commands/SyncIftaTaxRates/SyncIftaTaxRates.cs) | Command / import / data transformation |
| [Server/Application/Features/Fuel/Interfaces/IFuelDiscountProvider.cs](../../../Server/Application/Features/Fuel/Interfaces/IFuelDiscountProvider.cs) | Application interface |
| [Server/Application/Features/Fuel/Interfaces/IGmailWatchService.cs](../../../Server/Application/Features/Fuel/Interfaces/IGmailWatchService.cs) | Application interface |
| [Server/Application/Features/Fuel/Interfaces/IIftaApiService.cs](../../../Server/Application/Features/Fuel/Interfaces/IIftaApiService.cs) | Application interface |
| [Server/Application/Features/Fuel/Interfaces/IPlaceSearchService.cs](../../../Server/Application/Features/Fuel/Interfaces/IPlaceSearchService.cs) | Application interface |
| [Server/Application/Features/Fuel/Models/FuelDiscountImportData.cs](../../../Server/Application/Features/Fuel/Models/FuelDiscountImportData.cs) | Contract / DTO |
| [Server/Application/Features/Fuel/Models/FuelDiscountImportRow.cs](../../../Server/Application/Features/Fuel/Models/FuelDiscountImportRow.cs) | Contract / DTO |
| [Server/Application/Features/Fuel/Models/PlaceSearchResult.cs](../../../Server/Application/Features/Fuel/Models/PlaceSearchResult.cs) | Contract / DTO |
| [Server/Application/Features/Fuel/Queries/GetFuelStations/FuelPriceCalculator.cs](../../../Server/Application/Features/Fuel/Queries/GetFuelStations/FuelPriceCalculator.cs) | Data query / projection / cache |
| [Server/Application/Features/Fuel/Queries/GetFuelStations/GetFuelStations.cs](../../../Server/Application/Features/Fuel/Queries/GetFuelStations/GetFuelStations.cs) | Data query / projection / cache |
| [Server/Application/Features/Users/Commands/DeleteUser.cs](../../../Server/Application/Features/Users/Commands/DeleteUser.cs) | Command / import / data transformation |
| [Server/Application/Features/Users/Commands/RegisterUser.cs](../../../Server/Application/Features/Users/Commands/RegisterUser.cs) | Command / import / data transformation |
| [Server/Application/Features/Users/Commands/UpdateUser.cs](../../../Server/Application/Features/Users/Commands/UpdateUser.cs) | Command / import / data transformation |
| [Server/Application/Features/Users/Models/UserDto.cs](../../../Server/Application/Features/Users/Models/UserDto.cs) | Contract / DTO |
| [Server/Application/Features/Users/Queries/GetUserById.cs](../../../Server/Application/Features/Users/Queries/GetUserById.cs) | Data query / projection / cache |
| [Server/Application/Features/Users/Queries/GetUserList.cs](../../../Server/Application/Features/Users/Queries/GetUserList.cs) | Data query / projection / cache |
| [Server/Application/GlobalUsings.cs](../../../Server/Application/GlobalUsings.cs) | Supporting logic / documentation / startup |
| [Server/Application/Interfaces/IAppDbContext.cs](../../../Server/Application/Interfaces/IAppDbContext.cs) | Application interface |
| [Server/Application/Interfaces/IIdentityService.cs](../../../Server/Application/Interfaces/IIdentityService.cs) | Application interface |
| [Server/Application/Models/ListQuery.cs](../../../Server/Application/Models/ListQuery.cs) | Contract / DTO |
| [Server/Application/Models/PaginatedList.cs](../../../Server/Application/Models/PaginatedList.cs) | Contract / DTO |
| [Server/Application/Models/RequestResponse.cs](../../../Server/Application/Models/RequestResponse.cs) | Contract / DTO |
| [Server/Domain/Domain.csproj](../../../Server/Domain/Domain.csproj) | Configuration / build / deployment |
| [Server/Domain/Entities/BaseEntity.cs](../../../Server/Domain/Entities/BaseEntity.cs) | Domain entity |
| [Server/Domain/Entities/Dispatch/Customer.cs](../../../Server/Domain/Entities/Dispatch/Customer.cs) | Domain entity |
| [Server/Domain/Entities/Dispatch/Dispatch.cs](../../../Server/Domain/Entities/Dispatch/Dispatch.cs) | Domain entity |
| [Server/Domain/Entities/Dispatch/DispatchStop.cs](../../../Server/Domain/Entities/Dispatch/DispatchStop.cs) | Domain entity |
| [Server/Domain/Entities/Fleet/Driver.cs](../../../Server/Domain/Entities/Fleet/Driver.cs) | Domain entity |
| [Server/Domain/Entities/Fleet/Trailer.cs](../../../Server/Domain/Entities/Fleet/Trailer.cs) | Domain entity |
| [Server/Domain/Entities/Fleet/Truck.cs](../../../Server/Domain/Entities/Fleet/Truck.cs) | Domain entity |
| [Server/Domain/Entities/Fuel/FuelDiscount.cs](../../../Server/Domain/Entities/Fuel/FuelDiscount.cs) | Domain entity |
| [Server/Domain/Entities/Fuel/FuelImportSource.cs](../../../Server/Domain/Entities/Fuel/FuelImportSource.cs) | Domain entity |
| [Server/Domain/Entities/Fuel/FuelStation.cs](../../../Server/Domain/Entities/Fuel/FuelStation.cs) | Domain entity |
| [Server/Domain/Entities/Fuel/FuelTransaction.cs](../../../Server/Domain/Entities/Fuel/FuelTransaction.cs) | Domain entity |
| [Server/Domain/Entities/Fuel/IftaTaxRate.cs](../../../Server/Domain/Entities/Fuel/IftaTaxRate.cs) | Domain entity |
| [Server/Domain/Entities/Users.cs](../../../Server/Domain/Entities/Users.cs) | Domain entity |
| [Server/Infrastructure/DependencyInjection.cs](../../../Server/Infrastructure/DependencyInjection.cs) | Supporting logic / documentation / startup |
| [Server/Infrastructure/Identity/AppUser.cs](../../../Server/Infrastructure/Identity/AppUser.cs) | Authorization / accounts |
| [Server/Infrastructure/Identity/AuthService.cs](../../../Server/Infrastructure/Identity/AuthService.cs) | Authorization / accounts |
| [Server/Infrastructure/Identity/IdentityService.cs](../../../Server/Infrastructure/Identity/IdentityService.cs) | Authorization / accounts |
| [Server/Infrastructure/Identity/SessionValidationMiddleware.cs](../../../Server/Infrastructure/Identity/SessionValidationMiddleware.cs) | Authorization / accounts |
| [Server/Infrastructure/Infrastructure.csproj](../../../Server/Infrastructure/Infrastructure.csproj) | Configuration / build / deployment |
| [Server/Infrastructure/Integrations/Bvd/BvdFuelDiscountProvider.cs](../../../Server/Infrastructure/Integrations/Bvd/BvdFuelDiscountProvider.cs) | External provider adapter |
| [Server/Infrastructure/Integrations/Bvd/BvdFuelParser.cs](../../../Server/Infrastructure/Integrations/Bvd/BvdFuelParser.cs) | External provider adapter |
| [Server/Infrastructure/Integrations/Samsara/Models/SamsaraAssignment.cs](../../../Server/Infrastructure/Integrations/Samsara/Models/SamsaraAssignment.cs) | Contract / DTO |
| [Server/Infrastructure/Integrations/Samsara/Models/SamsaraDailyHosLog.cs](../../../Server/Infrastructure/Integrations/Samsara/Models/SamsaraDailyHosLog.cs) | Contract / DTO |
| [Server/Infrastructure/Integrations/Samsara/Models/SamsaraDriver.cs](../../../Server/Infrastructure/Integrations/Samsara/Models/SamsaraDriver.cs) | Contract / DTO |
| [Server/Infrastructure/Integrations/Samsara/Models/SamsaraLocationSpeedStream.cs](../../../Server/Infrastructure/Integrations/Samsara/Models/SamsaraLocationSpeedStream.cs) | Contract / DTO |
| [Server/Infrastructure/Integrations/Samsara/Models/SamsaraTag.cs](../../../Server/Infrastructure/Integrations/Samsara/Models/SamsaraTag.cs) | Contract / DTO |
| [Server/Infrastructure/Integrations/Samsara/Models/SamsaraTrailer.cs](../../../Server/Infrastructure/Integrations/Samsara/Models/SamsaraTrailer.cs) | Contract / DTO |
| [Server/Infrastructure/Integrations/Samsara/Models/SamsaraVehicle.cs](../../../Server/Infrastructure/Integrations/Samsara/Models/SamsaraVehicle.cs) | Contract / DTO |
| [Server/Infrastructure/Integrations/Samsara/Models/SamsaraVehicleLocation.cs](../../../Server/Infrastructure/Integrations/Samsara/Models/SamsaraVehicleLocation.cs) | Contract / DTO |
| [Server/Infrastructure/Integrations/Samsara/README.md](../../../Server/Infrastructure/Integrations/Samsara/README.md) | External provider adapter |
| [Server/Infrastructure/Integrations/Samsara/SamsaraApiService.cs](../../../Server/Infrastructure/Integrations/Samsara/SamsaraApiService.cs) | External provider adapter |
| [Server/Infrastructure/Integrations/Samsara/SamsaraFleetProvider.cs](../../../Server/Infrastructure/Integrations/Samsara/SamsaraFleetProvider.cs) | External provider adapter |
| [Server/Infrastructure/Integrations/Samsara/SamsaraFleetTelemetryProvider.cs](../../../Server/Infrastructure/Integrations/Samsara/SamsaraFleetTelemetryProvider.cs) | External provider adapter |
| [Server/Infrastructure/Integrations/Torque/Models/TorqueDispatchDto.cs](../../../Server/Infrastructure/Integrations/Torque/Models/TorqueDispatchDto.cs) | Contract / DTO |
| [Server/Infrastructure/Integrations/Torque/Models/TorqueDispatchResponse.cs](../../../Server/Infrastructure/Integrations/Torque/Models/TorqueDispatchResponse.cs) | Contract / DTO |
| [Server/Infrastructure/Integrations/Torque/Models/TorqueDispatchStopDto.cs](../../../Server/Infrastructure/Integrations/Torque/Models/TorqueDispatchStopDto.cs) | Contract / DTO |
| [Server/Infrastructure/Integrations/Torque/TorqueApiService.cs](../../../Server/Infrastructure/Integrations/Torque/TorqueApiService.cs) | External provider adapter |
| [Server/Infrastructure/Integrations/Torque/TorqueDispatchProvider.cs](../../../Server/Infrastructure/Integrations/Torque/TorqueDispatchProvider.cs) | External provider adapter |
| [Server/Infrastructure/Persistence/AppDbContext.cs](../../../Server/Infrastructure/Persistence/AppDbContext.cs) | Supporting logic / documentation / startup |
| [Server/Infrastructure/Persistence/AppDbContext.overrides.cs](../../../Server/Infrastructure/Persistence/AppDbContext.overrides.cs) | Supporting logic / documentation / startup |
| [Server/Infrastructure/Persistence/Configurations/Dispatch/CustomerConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/Dispatch/CustomerConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Configurations/Dispatch/DispatchConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/Dispatch/DispatchConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Configurations/Dispatch/DispatchStopConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/Dispatch/DispatchStopConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Configurations/Fleet/DriverConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/Fleet/DriverConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Configurations/Fleet/TrailerConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/Fleet/TrailerConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Configurations/Fleet/TruckConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/Fleet/TruckConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Configurations/Fuel/FuelDiscountConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/Fuel/FuelDiscountConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Configurations/Fuel/FuelImportSourceConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/Fuel/FuelImportSourceConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Configurations/Fuel/FuelStationConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/Fuel/FuelStationConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Configurations/Fuel/FuelTransactionConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/Fuel/FuelTransactionConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Configurations/Fuel/IftaTaxRateConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/Fuel/IftaTaxRateConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Configurations/UsersConfiguration.cs](../../../Server/Infrastructure/Persistence/Configurations/UsersConfiguration.cs) | EF mapping, indexes and constraints |
| [Server/Infrastructure/Persistence/Migrations/20260830231835_InitialCreate.Designer.cs](../../../Server/Infrastructure/Persistence/Migrations/20260830231835_InitialCreate.Designer.cs) | Generated EF model |
| [Server/Infrastructure/Persistence/Migrations/20260830231835_InitialCreate.cs](../../../Server/Infrastructure/Persistence/Migrations/20260830231835_InitialCreate.cs) | Database migration |
| [Server/Infrastructure/Persistence/Migrations/20260831043502_AddFleetEntities.Designer.cs](../../../Server/Infrastructure/Persistence/Migrations/20260831043502_AddFleetEntities.Designer.cs) | Generated EF model |
| [Server/Infrastructure/Persistence/Migrations/20260831043502_AddFleetEntities.cs](../../../Server/Infrastructure/Persistence/Migrations/20260831043502_AddFleetEntities.cs) | Database migration |
| [Server/Infrastructure/Persistence/Migrations/20260831043803_CheckFleetChanges.Designer.cs](../../../Server/Infrastructure/Persistence/Migrations/20260831043803_CheckFleetChanges.Designer.cs) | Generated EF model |
| [Server/Infrastructure/Persistence/Migrations/20260831043803_CheckFleetChanges.cs](../../../Server/Infrastructure/Persistence/Migrations/20260831043803_CheckFleetChanges.cs) | Database migration |
| [Server/Infrastructure/Persistence/Migrations/20260901144411_AddTruckLocationSavedAt.Designer.cs](../../../Server/Infrastructure/Persistence/Migrations/20260901144411_AddTruckLocationSavedAt.Designer.cs) | Generated EF model |
| [Server/Infrastructure/Persistence/Migrations/20260901144411_AddTruckLocationSavedAt.cs](../../../Server/Infrastructure/Persistence/Migrations/20260901144411_AddTruckLocationSavedAt.cs) | Database migration |
| [Server/Infrastructure/Persistence/Migrations/20260902090043_AddIftaTaxRates.Designer.cs](../../../Server/Infrastructure/Persistence/Migrations/20260902090043_AddIftaTaxRates.Designer.cs) | Generated EF model |
| [Server/Infrastructure/Persistence/Migrations/20260902090043_AddIftaTaxRates.cs](../../../Server/Infrastructure/Persistence/Migrations/20260902090043_AddIftaTaxRates.cs) | Database migration |
| [Server/Infrastructure/Persistence/Migrations/20260902091842_AddIftaTaxRateUnit.Designer.cs](../../../Server/Infrastructure/Persistence/Migrations/20260902091842_AddIftaTaxRateUnit.Designer.cs) | Generated EF model |
| [Server/Infrastructure/Persistence/Migrations/20260902091842_AddIftaTaxRateUnit.cs](../../../Server/Infrastructure/Persistence/Migrations/20260902091842_AddIftaTaxRateUnit.cs) | Database migration |
| [Server/Infrastructure/Persistence/Migrations/20260903120556_RemoveTruckLocationFields.Designer.cs](../../../Server/Infrastructure/Persistence/Migrations/20260903120556_RemoveTruckLocationFields.Designer.cs) | Generated EF model |
| [Server/Infrastructure/Persistence/Migrations/20260903120556_RemoveTruckLocationFields.cs](../../../Server/Infrastructure/Persistence/Migrations/20260903120556_RemoveTruckLocationFields.cs) | Database migration |
| [Server/Infrastructure/Persistence/Migrations/20260904025058_AddDispatch.Designer.cs](../../../Server/Infrastructure/Persistence/Migrations/20260904025058_AddDispatch.Designer.cs) | Generated EF model |
| [Server/Infrastructure/Persistence/Migrations/20260904025058_AddDispatch.cs](../../../Server/Infrastructure/Persistence/Migrations/20260904025058_AddDispatch.cs) | Database migration |
| [Server/Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs](../../../Server/Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs) | Generated EF model |
| [Server/Infrastructure/Integrations/Http/BaseApiService.cs](../../../Server/Infrastructure/Integrations/Http/BaseApiService.cs) | Application / infrastructure service |
| [Server/Infrastructure/Integrations/Google/Gmail/GmailAttachment.cs](../../../Server/Infrastructure/Integrations/Google/Gmail/GmailAttachment.cs) | Application / infrastructure service |
| [Server/Infrastructure/Integrations/Google/Gmail/GmailAttachmentService.cs](../../../Server/Infrastructure/Integrations/Google/Gmail/GmailAttachmentService.cs) | Application / infrastructure service |
| [Server/Infrastructure/Integrations/Google/Gmail/GmailServiceFactory.cs](../../../Server/Infrastructure/Integrations/Google/Gmail/GmailServiceFactory.cs) | Application / infrastructure service |
| [Server/Infrastructure/Integrations/Google/Gmail/GmailWatchService.cs](../../../Server/Infrastructure/Integrations/Google/Gmail/GmailWatchService.cs) | Application / infrastructure service |
| [Server/Infrastructure/Integrations/Ifta/IftaApiService.cs](../../../Server/Infrastructure/Integrations/Ifta/IftaApiService.cs) | Application / infrastructure service |
| [Server/Infrastructure/Integrations/Google/Places/GooglePlacesService.cs](../../../Server/Infrastructure/Integrations/Google/Places/GooglePlacesService.cs) | Application / infrastructure service |
