<# Build and deploy the MesDataManager web application #>

dotnet publish S:\WebApplications\MesDataManager\src\MesDataManager.Web -c Release -o S:\WebApplications-deploy\MesDataManager
S:\WebApplications\MesDataManager\deploy\Publish-ToIis.ps1 -SourcePath S:\WebApplications-deploy\MesDataManager -DestinationPath \\itbsintra01\c$\inetpub\wwwroot\MesDataManager