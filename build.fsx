  
#nowarn "213"
#if FAKE
#r "paket: groupref Build //"
#endif
#if !FAKE
#load @"./.paket/load/netstandard2.0/Build/build.group.fsx"
#endif
// #r "paket: groupref Build //"
// #load ".fake/build.fsx/intellisense.fsx"
    

open System
open System.IO
open Paket 
open Fake 
open Fake.Core.TargetOperators
open Fake.Core 
open Fake.IO
// open Fake.IO.Zip
open Fake.DotNet
open Fake.IO.Globbing.Operators
open Fake.IO.FileSystemOperators
// open Fake.IO.Path

let test = "/sub" @@ "/test"

Target.initEnvironment()

let config = DotNet.BuildConfiguration.Release
let setParams (p:DotNet.BuildOptions) = { p with Configuration = config }

let outputPath = __SOURCE_DIRECTORY__ @@ "bin"

// Read release notes & version info from RELEASE_NOTES.md
Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let release = ReleaseNotes.load "RELEASE_NOTES.md"

let packProvider (providerProject: string) (outDir: string) =
    let setParams (p:DotNet.PackOptions) = { p with OutputPath = Some outDir; Configuration = config}

    DotNet.pack setParams providerProject

Target.create "Clean" (fun _ ->
    !! "**/**/bin/" |> Shell.cleanDirs
    !! "**/**/obj/" |> Shell.cleanDirs
    
    Shell.cleanDirs ["bin"; "temp"]
)

Target.create "Build" (fun _ ->
    DotNet.build setParams "src/FSharp.TypeProviders.SDK.fsproj"
    DotNet.build setParams "tests/FSharp.TypeProviders.SDK.Tests.fsproj"
)

Target.create "Examples" (fun _ ->
    DotNet.build setParams "examples/BasicProvider.DesignTime/BasicProvider.DesignTime.fsproj"
    DotNet.build setParams "examples/BasicProvider.Runtime/BasicProvider.Runtime.fsproj"
    DotNet.build setParams "examples/StressProvider/StressProvider.fsproj"
)

Target.create "RunTests" (fun _ ->
    let setTestOptions (p:DotNet.TestOptions) =
        { p with Configuration = config }

    [
        "tests/FSharp.TypeProviders.SDK.Tests.fsproj"
        "examples/BasicProvider.Tests/BasicProvider.Tests.fsproj"
        "examples/StressProvider.Tests/StressProvider.Tests.fsproj"
    ]
    |> List.iter (DotNet.test setTestOptions)
)

Target.create "Pack" (fun _ ->
    let releaseNotes = String.toLines release.Notes
    let setParams (p:DotNet.PackOptions) = { p with OutputPath = Some outputPath; Configuration = config}

    DotNet.pack  (fun p -> { 
        setParams p with 
            MSBuildParams = { 
                MSBuild.CliArguments.Create() with
                    Properties = [
                        "PackageVersion", release.NugetVersion
                        "ReleaseNotes", releaseNotes
                    ] 
            } 
        }) "src/FSharp.TypeProviders.SDK.fsproj"

    packProvider "examples/BasicProvider.Runtime/BasicProvider.Runtime.fsproj" outputPath
    packProvider "examples/StressProvider/StressProvider.fsproj" outputPath

    DotNet.pack (fun p -> { 
        setParams p with 
            MSBuildParams = { 
                MSBuild.CliArguments.Create() with
                    Properties = [
                        "PackageVersion", release.NugetVersion
                        "ReleaseNotes", releaseNotes
                    ]
            } 
        }) "templates/FSharp.TypeProviders.Templates.proj"
)

Target.create "TestTemplatesNuGet" (fun _ ->
    let wd = __SOURCE_DIRECTORY__ @@ "temp"
    let runInTempDir (p:DotNet.Options) = { p with WorkingDirectory = wd }

    DotNet.exec runInTempDir "new" "-u FSharp.TypeProviders.Templates" |> ignore
    DotNet.exec runInTempDir "new" ("-i " + Path.Combine(outputPath, "FSharp.TypeProviders.Templates." + release.NugetVersion + ".nupkg")) |> ignore

    // Instantiate the template into a randomly generated name
    let ticks = let now = DateTime.Now in now.Ticks
    let testAppName = "tp2" + string (abs (hash ticks) % 100)
    let testAppDir = wd @@ testAppName
    let testAppBinDir = testAppDir @@ "bin"
    let testAppProj = testAppDir @@ "src" @@ $"{testAppName}.Runtime" @@ $"{testAppName}.Runtime.fsproj"
    Shell.cleanDir testAppDir
    DotNet.exec runInTempDir "new" (sprintf "typeprovider -n %s -lang F#" testAppName) |> ignore

    let runInTestAppDir (p:DotNet.Options) = { p with WorkingDirectory = testAppDir }
    DotNet.exec runInTestAppDir "tool" "restore" |> ignore
    DotNet.exec runInTestAppDir "paket" "restore" |> ignore
    DotNet.exec runInTestAppDir "build" "-c debug" |> ignore
    DotNet.exec runInTestAppDir "test" "-c debug" |> ignore

    DotNet.exec runInTempDir "new" "-u FSharp.TypeProviders.Templates" |> ignore

    DotNet.exec runInTestAppDir "build" "-c release" |> ignore
    packProvider testAppProj testAppBinDir
    let builtNugetPath =
        !! (testAppBinDir @@ "*.nupkg")
        // |> GlobbingPattern.setBaseDir baseDir
        |> Seq.head

    Zip.unzip (testAppBinDir @@ "nuget-extract") builtNugetPath
    try
        !! (testAppBinDir @@ "nuget-extract" @@ "lib" @@ "netstandard2.0" @@ $"{testAppName}.Runtime.dll") |> Seq.head |> ignore
        !! (testAppBinDir @@ "nuget-extract" @@ "typeproviders" @@ "fsharp41" @@ "netstandard2.0" @@ $"{testAppName}.DesignTime.dll") |> Seq.head |> ignore
    with
        | ex -> ex.Message |> failwith "Expected component not found in built nupkg of template test. %s"

    (* Manual steps without building nupkg
        dotnet pack src\FSharp.TypeProviders.SDK.fsproj /p:PackageVersion=0.0.0.99 --output bin -c release
        .nuget\nuget.exe pack -OutputDirectory bin -Version 0.0.0.99 templates/FSharp.TypeProviders.Templates.nuspec
        dotnet new -i  bin/FSharp.TypeProviders.Templates.0.0.0.99.nupkg
        dotnet new typeprovider -n tp3 -lang:F#
        *)
)

Target.create "DoNothing" ignore
Target.create "All" ignore

"Clean"
  ?=> "Build"
  ?=> "Examples"
  ?=> "RunTests"
  ?=> "Pack"
  ?=> "TestTemplatesNuGet"


"Clean" ==> "All"
"Build" ==> "All"
"Examples" ==> "All"
"RunTests" ==> "All"
"Pack" ==> "All"
"TestTemplatesNuGet" ==> "All"

Target.runOrDefault "All"
