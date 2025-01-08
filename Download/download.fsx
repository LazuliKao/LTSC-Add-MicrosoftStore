#r "nuget: StoreLib, 1.2.1"
#r "nuget: ShellProgressBar, 5.2.0"
#r "nuget: Downloader, 3.3.1"

open StoreLib.Services
open StoreLib.Models
open System.IO
open ShellProgressBar
open System
open Downloader

let dcathandler =
    // new DisplayCatalogHandler(DCatEndpoint.Production, new Locale(Market.US, Lang.en, true))
    DisplayCatalogHandler.ProductionConfig()

let rec formatByte (bytes) =
    let suffixes = [| "B"; "KB"; "MB"; "GB"; "TB"; "PB"; "EB"; "ZB"; "YB" |]

    let rec loop bytes (suffixes: string[]) (index: int) =
        if bytes < 1024.0 then
            sprintf "%.1f %s" bytes suffixes[index]
        else
            loop (bytes / 1024.0) suffixes (index + 1)

    loop bytes suffixes 0

let query () =
    task {
        do! dcathandler.QueryDCATAsync "9WZDNCRFJBMP"
        let! downloads = dcathandler.GetPackagesForProductAsync()
        return downloads |> List.ofSeq
    }

let downloadOne dir (package: PackageInstance) (update: string -> unit) (progress: float -> unit) =
    task {
        // printfn "Start: %s %A %A" (package.PackageMoniker) (package.PackageType) (package.PackageUri)
        let url = package.PackageUri
        let downloadOpt = DownloadConfiguration()
        use downloader = new DownloadService(downloadOpt)
        downloader.DownloadStarted.Add(fun (e) -> update ("Started" + e.FileName))

        downloader.DownloadProgressChanged.Add(fun (e) ->
            update (
                sprintf
                    "Progress: %.1f %s/%s %s/s"
                    (e.ProgressPercentage)
                    (e.ReceivedBytesSize |> float |> formatByte)
                    (e.TotalBytesToReceive |> float |> formatByte)
                    (e.BytesPerSecondSpeed |> formatByte)
            )

            progress e.ProgressPercentage)

        downloader.DownloadFileCompleted.Add(fun _ -> update ("Completed"))
        let dirInfo = new DirectoryInfo(dir)
        do! downloader.DownloadFileTaskAsync(url.ToString(), dirInfo)
    }

let downloads (mainBar: ProgressBar) (spawn: string -> ChildProgressBar) =
    task {
        let! downloads = query ()
        let packageDirectory = Path.Combine(Directory.GetCurrentDirectory(), "packages")

        if packageDirectory |> Directory.Exists |> not then
            packageDirectory |> Directory.CreateDirectory |> ignore

        let count = downloads |> List.length
        let i = ref 0

        for download in downloads do
            let current = i.Value
            i.Value <- i.Value + 1
            // printfn "%s %A %A" download.PackageMoniker download.PackageType download.PackageUri
            sprintf "%d / %d" current count |> mainBar.Tick
            use bar = spawn download.PackageMoniker
            do! downloadOne packageDirectory download (fun (s) -> bar.Tick(s)) (fun (s) -> bar.Tick(s |> int))
    }



let downloadProgress () =
    task {
        let opt =
            new ProgressBarOptions(
                ProgressCharacter = '─',
                ForegroundColor = ConsoleColor.Yellow,
                BackgroundColor = ConsoleColor.DarkYellow,
                DisableBottomPercentage = true,
                DisplayTimeInRealTime = true,
                CollapseWhenFinished = false
            )

        let childOptions =
            new ProgressBarOptions(
                ForegroundColor = ConsoleColor.Green,
                BackgroundColor = ConsoleColor.DarkGreen,
                ProgressCharacter = '─',
                DisableBottomPercentage = true,
                DisplayTimeInRealTime = true
            )

        let totalTicks = 10
        use bar = new ProgressBar(totalTicks, "Fetching...", opt)
        do! downloads bar (fun (name) -> bar.Spawn(totalTicks, "Downloading... " + name, childOptions))
    }

downloadProgress () |> Async.AwaitTask |> Async.RunSynchronously
