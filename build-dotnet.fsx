//#r @"packages/build/FAKE/tools/FakeLib.dll"
#r "paket: groupref build //"
#load "./.fake/build-dotnet.fsx/intellisense.fsx"

#if !FAKE
#r "netstandard"
#r "Facades/netstandard" // https://github.com/ionide/ionide-vscode-fsharp/issues/839#issuecomment-396296095
#endif


open System
//open Fake
open Fake.DotNet
open Fake.Core
open Fake.IO
//C:\development\comit\templates\saturnapp\paket-files\build\CompositionalIT\fshelpers\src\FsHelpers\ArmHelper
//#load @"paket-files/build/CompositionalIT/fshelpers/src/FsHelpers/ArmHelper/ArmHelper.fs"
#load @"C:\development\comit\templates\saturnapp\paket-files\build\CompositionalIT\fshelpers\src\FsHelpers\ArmHelper\ArmHelper.fs"
open Cit.Helpers.Arm
open Cit.Helpers.Arm.Parameters
open Microsoft.Azure.Management.ResourceManager.Fluent.Core

let appPath = "./src/Template.Saturn.WebHost/" |> Fake.IO.Path.getFullName
let infrastructureTestsPath = "./src/Template.Saturn.Infrastructure.Tests" |> Fake.IO.Path.getFullName
//TODO you wlll need to fill this in if using Fable and SAFE Stack
//let serverPath = Path.getFullName "./src/Server"
let clientPath = Path.getFullName "./src/Template.Saturn.Client"
let deployDir = Path.getFullName "./deploy"

let platformTool tool winTool =
    let tool = if Environment.isUnix then tool else winTool
    match ProcessUtils.tryFindFileOnPath tool with
    | Some t -> t
    | _ ->
        let errorMsg =
            tool + " was not found in path. " +
            "Please install it and make sure it's available from your path. " +
            "See https://safe-stack.github.io/docs/quickstart/#install-pre-requisites for more info"
        failwith errorMsg

let runDotNet cmd workingDir =
    let result =
        DotNet.exec (DotNet.Options.withWorkingDirectory workingDir) cmd ""
    if result.ExitCode <> 0 then failwithf "'dotnet %s' failed in %s" cmd workingDir

let runTool cmd args workingDir =
    let arguments = args |> String.split ' ' |> Arguments.OfArgs
    Command.RawCommand (cmd, arguments)
    |> CreateProcess.fromCommand
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore

let openBrowser url =
    //https://github.com/dotnet/corefx/issues/10361
    Command.ShellCommand url
    |> CreateProcess.fromCommand
    |> CreateProcess.ensureExitCodeWithMessage "opening browser failed"
    |> Proc.run
    |> ignore

let nodeTool = platformTool "node" "node.exe"
let yarnTool = platformTool "yarn" "yarn.cmd"

Target.create "InstallDotNetCore" (fun _ ->
    DotNet.install (fun p -> {p with Version = DotNet.CliVersion.GlobalJson }) |> ignore
)

Target.create "InstallClient" (fun _ ->
    printfn "Node version:"
    runTool nodeTool "--version" __SOURCE_DIRECTORY__
    printfn "Yarn version:"
    runTool yarnTool "--version" __SOURCE_DIRECTORY__
    runTool yarnTool "install --frozen-lockfile" __SOURCE_DIRECTORY__
    //TODO you will need this to pack fable
    runDotNet "restore" clientPath 
)

Target.create "Restore" (fun _ ->
    DotNet.restore (fun p -> p) appPath |> ignore
)

open Fake.IO.FileSystemOperators

Target.create "RenameConfig" (fun _ ->
    if not (File.exists(appPath @@ "config.yaml"))
        then Fake.IO.Shell.rename (appPath @@ "config.yaml") (appPath @@ "config_design.yaml") |> ignore
)

Target.create "Build"  (fun _ ->
    runDotNet "build" appPath
    //TODO you will need this for packing up fable
    //runTool yarnTool "webpack-cli --config src/Template.Saturn.Client/webpack.config.js -p" __SOURCE_DIRECTORY__
    runTool yarnTool (sprintf "webpack-cli --config %s -p" (appPath @@ "webpack.config.js")) __SOURCE_DIRECTORY__
)

Target.create "Run" (fun _ -> 
  let server = async {
    runDotNet "watch run" appPath |> ignore
    }

  //TODO you will need this to pack the client if you use safe stack
  let client = async {
        //runTool yarnTool "webpack-dev-server --config src/Template.Saturn.Client/webpack.config.js" __SOURCE_DIRECTORY__
        runTool yarnTool (sprintf "webpack-cli --config %s -p" (clientPath @@ "webpack.config.js")) __SOURCE_DIRECTORY__

    }

  let browser = async {
    Threading.Thread.Sleep 8000
    openBrowser "http://saturn.local:8085" |> ignore
  }
  let vsCodeSession = Environment.hasEnvironVar "vsCodeSession"
  let safeClientOnly = Environment.hasEnvironVar "safeClientOnly"

  //let tasks =
  //  [ if not safeClientOnly then yield server
  //    yield client
  //    if not vsCodeSession then yield browser ]


  [ client; server; browser]
  |> Async.Parallel
  |> Async.RunSynchronously
  |> ignore
)

Target.create "Bundle" (fun _ ->
    runDotNet (sprintf "publish \"%s\%s\" -c release -o \"%s\"" appPath "Template.Saturn.WebHost.fsproj" deployDir) __SOURCE_DIRECTORY__
    Shell.copyDir (Path.combine deployDir "public") (Path.combine clientPath "public") FileFilter.allFiles
)

type ArmOutput =
    { WebAppName : ParameterValue<string>
      WebAppPassword : ParameterValue<string> }
let mutable deploymentOutputs : ArmOutput option = None

Target.create "ArmTemplate" (fun _ ->
    let environment = Environment.environVarOrDefault "environment" (Guid.NewGuid().ToString().ToLower().Split '-' |> Array.head)
    let armTemplate = @"arm-template.json"
    //let resourceGroupName = "safe-" + environment
    let resourceGroupName =
        match Environment.environVar "resourceGroupName" with
        | name when String.IsNullOrEmpty(name) -> "experimental-deploy"
        | name -> name
        //"experimental-build"
    Trace.tracefn "RESOURCE GROUP IS %s" resourceGroupName

    let authCtx =
        // You can safely replace these with your own subscription and client IDs hard-coded into this script.
        let subscriptionId = try Environment.environVar "subscriptionId" |> Guid.Parse with _ -> failwith "Invalid Subscription ID. This should be your Azure Subscription ID."
        let clientId = try Environment.environVar "clientId" |> Guid.Parse with _ -> failwith "Invalid Client ID. This should be the Client ID of a Native application registered in Azure with permission to create resources in your subscription."

        Trace.tracefn "Deploying template '%s' to resource group '%s' in subscription '%O'..." armTemplate resourceGroupName subscriptionId
        subscriptionId
        |> authenticateDevice Trace.trace { ClientId = clientId; TenantId = None }
        |> Async.RunSynchronously

    let deployment =
        let location = Environment.environVarOrDefault "location" Region.EuropeWest.Name
        let pricingTier = Environment.environVarOrDefault "pricingTier" "F1"
        { DeploymentName = "SATURN-template-deploy"
          ResourceGroup = New(resourceGroupName, Region.Create location)
          ArmTemplate = IO.File.ReadAllText armTemplate
          Parameters =
              Simple
                  [ "environment", ArmString environment
                    "location", ArmString location
                    "pricingTier", ArmString pricingTier ]
          DeploymentMode = Incremental }

    deployment
    |> deployWithProgress authCtx
    |> Seq.iter(function
        | DeploymentInProgress (state, operations) -> Trace.tracefn "State is %s, completed %d operations." state operations
        | DeploymentError (statusCode, message) -> Trace.traceError <| sprintf "DEPLOYMENT ERROR: %s - '%s'" statusCode message
        | DeploymentCompleted d -> deploymentOutputs <- d)
)

open Fake.IO.Globbing.Operators
open System.Net

// https://github.com/SAFE-Stack/SAFE-template/issues/120
// https://stackoverflow.com/a/6994391/3232646
type TimeoutWebClient() =
    inherit WebClient()
    override this.GetWebRequest uri =
        let request = base.GetWebRequest uri
        request.Timeout <- 30 * 60 * 1000
        request

Target.create "AppService" (fun _ ->
    let zipFile = "deploy.zip"
    IO.File.Delete zipFile
    Zip.zip deployDir zipFile !!(deployDir + @"\**\**")

    let appName = deploymentOutputs.Value.WebAppName.value
    let appPassword = deploymentOutputs.Value.WebAppPassword.value

    let destinationUri = sprintf "https://%s.scm.azurewebsites.net/api/zipdeploy" appName
    let client = new TimeoutWebClient(Credentials = NetworkCredential("$" + appName, appPassword))
    Trace.tracefn "Uploading %s to %s" zipFile destinationUri
    client.UploadData(destinationUri, IO.File.ReadAllBytes zipFile) |> ignore
    )


Target.create "Test" (fun _ -> 
    DotNet.test (fun p -> p) infrastructureTestsPath
)

Target.create "Clean" (fun _ ->
    () //TODO cleanup the build folder
)

Target.create "Publish" (fun _ ->
    DotNet.publish (fun p -> { p with OutputPath = Some "../../published"} ) appPath
)

open Fake.Core.TargetOperators


"Clean" 
  ==> "InstallDotNetCore"
  ==> "InstallClient"
  ==> "RenameConfig"
  ==> "Build"

"Clean"
    ==> "InstallClient"
    ==> "Build"
    ==> "Bundle"
    ==> "ArmTemplate"
    ==> "AppService"

"Clean"
  ==> "InstallClient"
  ==> "Restore"
  ==> "Run"

"Clean"
  ==> "InstallDotNetCore"
  ==> "InstallClient"
  ==> "RenameConfig"
  ==> "Build"
  ==> "Test"

"Clean"
  ==> "InstallDotNetCore"
  ==> "RenameConfig"
  ==> "Build"
  ==> "Test"
  ==> "Publish"

Target.runOrDefaultWithArguments "Test"

