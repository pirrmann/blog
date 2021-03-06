#r "packages/FAKE/tools/FakeLib.dll"
#r "packages/WinSCP/lib/WinSCPnet.dll"

open Fake
open WinSCP
open System
open System.IO
open System.Diagnostics

module WindowsPath =
    open System
    open System.IO

    let private envVarOrEmpty name =
        let value = Environment.GetEnvironmentVariable(name)
        if isNull name then "" else value
    let path = lazy (
        (envVarOrEmpty "PATH").Split(Path.PathSeparator) |> List.ofArray)
    let pathExt = lazy (
        if Environment.OSVersion.Platform = PlatformID.Win32NT then
            (envVarOrEmpty "PATHEXT").Split(Path.PathSeparator) |> List.ofArray
        else
            [""]
        )

    let find names =
        path.Value
        |> Seq.collect (fun dir -> names |> List.map (fun name -> Path.Combine(dir, name)))
        |> Seq.tryFind(File.Exists)

    let findProgram name =
        pathExt.Value
        |> List.map ((+) name)
        |> find

let rootDir = Path.GetFullPath(__SOURCE_DIRECTORY__)

let getBundlePath() =
    match WindowsPath.findProgram "bundle" with
    | Some(p) -> p
    | None -> failwith "Bundle not found"

let execBundle args =
    let config (psi:ProcessStartInfo) =
        psi.FileName <- getBundlePath()
        psi.Arguments <- args
    let result = ExecProcess config (TimeSpan.MaxValue)
    if result <> 0 then failwith "Bundle failed"

Target "Install" <| fun _ ->
    execBundle ""

let drafts = environVarOrNone "DRAFTS" <> None
let future = environVarOrNone "FUTURE" <> None

Target "Build" <| fun _ ->
    execBundle
        (sprintf "exec jekyll build %s %s"
            (if drafts then "--drafts" else "")
            (if future then "--future" else ""))

Target "Serve" <| fun _ ->
    execBundle "exec jekyll serve --future --drafts --watch"

let private winScpPath =
    lazy (
        let assemblyDir = Path.GetDirectoryName(typedefof<Session>.Assembly.Location)
        Path.Combine(assemblyDir, "..", "content", "WinSCP.exe")
    )

let uploadFolder localDir remoteDir (options: SessionOptions) =
    use session = new Session()
    session.ExecutablePath <- winScpPath.Value
    session.Open options

    let localPath = Path.Combine(localDir, "*")
    printfn "Uploading the content of '%s' to '%s' on '%s'" localPath remoteDir options.HostName
    let result = session.PutFiles(localPath, remoteDir)

    if not result.IsSuccess then
        let exceptions = result.Failures |> Seq.map (fun e -> e :> Exception)
        raise (AggregateException(exceptions))

let envVarOrAskUser name question =
    match environVarOrNone name with
    | Some x -> x
    | None -> getUserPassword question

Target "Upload" <| fun _ ->
    let options = SessionOptions()
    options.Protocol <- Protocol.Ftp
    options.FtpSecure <- FtpSecure.Explicit
    options.FtpMode <- FtpMode.Active
    options.HostName <- "vbfox.net"
    options.UserName <- "blog_upload"
    options.Password <- envVarOrAskUser "password" "FTP Password: "
    uploadFolder (rootDir </> "_site") "/" options

Target "CI" DoNothing

"Install" ?=> "Build"
"Install" ?=> "Serve"
"Build" ==> "Upload"

"Install" ==> "CI"
"Upload" ==> "CI"

RunTargetOrDefault "Build"
