namespace AzureFunctionFSharp

open System;
open System.Collections.Generic
open Microsoft.Azure.WebJobs;
open Microsoft.Azure.WebJobs.Extensions.Http
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.Logging;
open Hopac
open HttpFs.Client
open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

module UpNext=

    let loadFile url =
        Request.createUrl Get url
        |> Request.responseAsString
        |> run

    let private deserializer = DeserializerBuilder().WithNamingConvention( CamelCaseNamingConvention()).IgnoreUnmatchedProperties().Build()

    let deserialize<'t> document =
        use input = new IO.StringReader(document)
        let result = deserializer.Deserialize<'t>(input)
        result

    [<CLIMutable>]
    type Talk = {
        Title: string
        Date: DateTime
    }
    [<CLIMutable>]
    type Speaker = {
        Name: string
        Bio: string
        Talks: Talk array
    }

    type TalkResponse = {
        Speaker: string
        Title: string
        Date: DateTime
    }

    let getNextTalks currentDate talks  =
        talks
        |> Seq.sortBy (fun s -> s.Date)
        |> Seq.filter(fun s -> s.Date >= currentDate)
        |> (fun ts ->
            match Seq.length ts with
            | l when l >= 3-> Seq.take 3 ts
            | _ -> ts
            )

    let formatTalk t = sprintf "* *%s:* %s @ %s " t.Speaker t.Title (t.Date.ToShortTimeString())

    let formatTalks talks =
        talks
        |> Seq.map formatTalk
        |> (fun fts ->
            if Seq.isEmpty fts then
                let now = DateTime.Now
                let noMoreTalks = sprintf "There aren't any talks coming up as of %s @ %s" (now.ToShortDateString()) (now.ToShortTimeString())
                seq { yield noMoreTalks }
            else fts)

    let talksToSlackFormat = ((getNextTalks (DateTime.Now)) >> formatTalks >>(fun talks -> String.Join("\n", talks)))

    [<FunctionName("UpNext")>]
    let Run([<HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)>] req: HttpRequest, log: ILogger)=

        log.LogInformation("F# HTTP trigger function processed a request.")

        let speakers =
            loadFile "https://raw.githubusercontent.com/open-fsharp/open-fsharp.github.io/master/_data/speakers.yml"
            |> deserialize<Dictionary<int, Speaker array>>

        let currentYear = speakers.[2019]

        currentYear
        |> Array.filter (fun s -> (not (isNull s.Talks)))
        |> Array.map (fun s -> s.Talks |> Array.map (fun t -> { Speaker = s.Name; Title = t.Title; Date = t.Date }))
        |> Array.collect id
        |> talksToSlackFormat
        |> ObjectResult