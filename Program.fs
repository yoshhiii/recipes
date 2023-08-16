module Recipes.Program

open System
open FSharp.Data
open Falco
open Falco.Markup
open Falco.Routing
open Falco.HostBuilder

let form =
    Templates.html5
        "en"
        []
        [ Elem.form
              [ Attr.method "post" ]
              [ Elem.input [ Attr.name "url" ]
                Elem.input [ Attr.type' "submit"; Attr.value "Scrape me" ] ] ]

let formWithResult (recipeContent: HtmlNode list) =
    Templates.html5
        "en"
        []
        [ Elem.form
              [ Attr.method "post" ]
              [ Elem.input [ Attr.name "url" ]
                Elem.input [ Attr.type' "submit"; Attr.value "Scrape me" ] ]
          Elem.h2 [] [ Text.raw "Ingredients"]
          Elem.ul [] (recipeContent |> List.map (fun x -> Elem.li [] [ Text.raw (x.InnerText())]))
        ]
   
let scrapeUrl (url: string) =
    // let testUrl = "https://www.thepioneerwoman.com/food-cooking/recipes/a32436513/beef-and-broccoli-stir-fry-recipe/"
    let results = Http.RequestString(url, silentCookieErrors = true)
    let page = HtmlDocument.Parse(results)
    
    let data = page.Descendants ["ul"]
            |> Seq.filter (fun x ->
                let classList = x.TryGetAttribute "class"
                match classList with
                | Some id ->
                    id.Value().Contains("ingredient")
                | _ -> false
                ) // have the ul that should contain ingredient li
            |> Seq.map (fun x -> x.Descendants ["li"] |> Seq.toList)
            |> Seq.concat
            |> Seq.toList
    data
    

let handle: HttpHandler =
    fun ctx ->
        task {
            let! f = Request.getForm ctx
            let url = f.GetString("url", "")
            let data = scrapeUrl(url)

            return! Response.ofHtml (formWithResult data) ctx
        }

[<EntryPoint>]
let main args =
    webHost args { endpoints [ all "/" [ GET, Response.ofHtml form; POST, handle ] ] }
    0
