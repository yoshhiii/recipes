module Recipes.Program

open System
open FSharp.Control
open FSharp.CosmosDb
open FSharp.Data
open Falco
open Falco.Markup
open Falco.Routing
open Falco.HostBuilder
open Microsoft.Extensions.DependencyInjection
open Recipes.UI

let form =
    Elem.form
        [ Attr.method "post" ]
        [ Elem.input [ Attr.name "url" ]
          Elem.input [ Attr.type' "submit"; Attr.value "Scrape me"; Attr.class' "rounded-md bg-red-100 border-black border "] ]
let view =
    let title = "Scrape a recipe"
    Layouts.master title [
        Elem.div [] [form]
    ]

let formWithResult (recipeContent: HtmlNode list) =
    Templates.html5
        "en"
        []
        [ Elem.form
              [ Attr.method "post" ]
              [ Elem.input [ Attr.name "url" ]
                Elem.input [ Attr.type' "submit"; Attr.value "Scrape me" ] ]
          Elem.h2 [] [ Text.raw "Ingredients" ]
          Elem.ul [] (recipeContent |> List.map (fun x -> Elem.li [] [ Text.raw (x.InnerText()) ])) ]

let scrapeUrl (url: string) =
    // let testUrl = "https://www.thepioneerwoman.com/food-cooking/recipes/a32436513/beef-and-broccoli-stir-fry-recipe/"
    let results = Http.RequestString(url, silentCookieErrors = true)
    let page = HtmlDocument.Parse(results)

    let data =
        page.Descendants [ "ul" ]
        |> Seq.filter (fun x ->
            let classList = x.TryGetAttribute "class"

            match classList with
            | Some id -> id.Value().Contains("ingredient")
            | _ -> false) // have the ul that should contain ingredient li
        |> Seq.map (fun x -> x.Descendants [ "li" ] |> Seq.toList)
        |> Seq.concat
        |> Seq.toList

    data


let handle: HttpHandler =
    fun ctx ->
        task {
            let! f = Request.getForm ctx
            let url = f.GetString("url", "")
            let data = scrapeUrl (url)

            return! Response.ofHtml (formWithResult data) ctx
        }


type Recipe =
    { id: Guid
      name: string
      ingredients: string list
      instructions: string list
      source: string option }  // it might be nice to link to original source if importing

module Db =
    let getRecipesConnection uri key =
        uri
        |> Cosmos.host
        |> Cosmos.connect key
        |> Cosmos.database "RecipeBox"
        |> Cosmos.container "Recipes"

    module RecipeStore =
        let getRecipes conn =
            conn
            |> Cosmos.query<Recipe> "SELECT * FROM c"
            |> Cosmos.execAsync
            |> AsyncSeq.toListAsync

        let createRecipe conn recipe =
            conn |> Cosmos.insert<Recipe> recipe |> Cosmos.execAsync |> AsyncSeq.toListAsync

        let updateRecipe conn recipe pk =
            conn
            |> Cosmos.update<Recipe> (recipe.id.ToString()) pk (fun r ->
                { r with
                    ingredients = recipe.ingredients
                    instructions = recipe.instructions
                    name = recipe.name })

module UI =
    let recipesList (recipes: Recipe list) =
        Elem.ul [] (recipes |> List.map (fun r -> Elem.li [] [ Text.raw r.name ]))

    // a future "edit" route could use this same template if you just take the action url as an arg
    let createRecipe (recipe: Recipe) =
        Elem.form
            [ Attr.method "POST"; Attr.action "/recipes" ]
            [ Elem.input [ Attr.type' "hidden"; Attr.name "recipe_id"; Attr.value (string recipe.id) ]
              Elem.label [] [
                  Text.raw "Recipe Name"
                  Elem.input [ Attr.type' "text"; Attr.name "recipe_name" ] ]
              Elem.label [] [
                  Text.raw "Recipe Source"
                  Elem.input [ Attr.type' "text"; Attr.name "recipe_source"; Attr.placeholder "ie. Mom, https://somefoodblogger.com, etc..." ] ]
              Elem.label [] [
                  Text.raw "Ingredients"
                  Elem.textarea [ Attr.name "recipe_ingredients" ] [] ]
              Elem.label [] [
                  Text.raw "Instructions"
                  Elem.textarea [ Attr.name "recipe_instructions" ] [] ]
              Elem.button [] [ Text.raw "Add to Recipe Box" ] ]

module RecipesController =
    open Db
    
    let private splitLines (s: string) =
        s.Replace("\r", "").Split("\n")
        |> Seq.where (fun v -> 
            String.IsNullOrWhiteSpace(v) |> not
        )
        |> Seq.toList
        
    let private getOptionalString (s: string) =
        if String.IsNullOrWhiteSpace(s) then
            None
        else
            Some(s)

    let index: HttpHandler =
        fun ctx ->
            task {
                let conn = ctx.GetService<ConnectionOperation>()
                let! recipes = RecipeStore.getRecipes conn
                return Response.ofHtml (UI.recipesList recipes) ctx
            }

    let save: HttpHandler =
        fun ctx ->
            task {
                let! form = Request.getForm ctx

                let recipe: Recipe =
                    { id = form.GetGuid "recipe_id"
                      name = form.GetString "recipe_name"
                      ingredients = form.GetString "recipe_ingredients" |> splitLines
                      instructions = form.GetString "recipe_instructions" |> splitLines
                      source = form.GetString "recipe_source" |> getOptionalString }

                let conn = ctx.GetService<ConnectionOperation>()
                // There must be a way to just await this without having to assign a variable
                // let! recipes = RecipeStore.createRecipe conn recipe
                // Not sure if this is better or worse lol, but I'm guessing we would want to do something
                // with a response at some point? 
                RecipeStore.createRecipe conn recipe |> Async.RunSynchronously |> ignore

                return Response.redirectTemporarily "/recipes" ctx
            }

    let create: HttpHandler =
        fun ctx ->
            let recipe: Recipe =
                { id = Guid.NewGuid()
                  name = String.Empty
                  ingredients = []
                  instructions = []
                  source = Some(String.Empty) }

            Response.ofHtml (UI.createRecipe recipe) ctx

[<EntryPoint>]
let main args =
    let env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")

    let config =
        configuration [||] {
            add_env
            required_json "appsettings.json"
            optional_json (String.Concat([| "appsettings."; env; ".json" |]))
        }

    let cosmosUri = config.["Cosmos:Uri"]
    let cosmosKey = config.["Cosmos:Key"]

    // I don't know about this
    let dbConnectionService (svc: IServiceCollection) =
        svc.AddSingleton<ConnectionOperation>(fun _ -> Db.getRecipesConnection cosmosUri cosmosKey)

    if String.IsNullOrWhiteSpace(cosmosUri) || String.IsNullOrWhiteSpace(cosmosKey) then
        failwith "Invalid cosmos configuration"

    webHost args {
        use_static_files
        add_service dbConnectionService
        endpoints
            [ all "/" [ GET, Response.ofHtml view; POST, handle ]
              all "/recipes" [ GET, RecipesController.index; POST, RecipesController.save ]
              get "/recipes/new" RecipesController.create ]
    }

    0
