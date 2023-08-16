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


type Recipe =
    { id: Guid
      name: string
      ingredients: List<string>
      instructions: List<string> }

module Db =
    let getRecipesConnection uri key=
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
              Elem.input [ Attr.type' "text"; Attr.name "recipe_name" ]
              Elem.button [] [ Text.raw "Add to Recipe Box" ] ]

module RecipesController =
    open Db

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
                      ingredients = form.GetStringArray "ingredient[]" |> Array.toList
                      instructions = form.GetStringArray "instruction[]" |> Array.toList }

                let conn = ctx.GetService<ConnectionOperation>()
                // There must be a way to just await this without having to assign a variable
                let! recipes = RecipeStore.createRecipe conn recipe

                return Response.redirectTemporarily "/recipes" ctx
            }

    let create: HttpHandler =
        fun ctx ->
            let recipe: Recipe =
                { id = Guid.NewGuid()
                  name = ""
                  ingredients = []
                  instructions = [] }

            Response.ofHtml (UI.createRecipe recipe) ctx
            
[<EntryPoint>]
let main args =
    let env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    
    let config = configuration [||] {
        add_env
        required_json "appsettings.json"
        optional_json (String.Concat([|"appsettings."; env; ".json"|]))
    }
    
    let cosmosUri = config.["Cosmos:Uri"]
    let cosmosKey = config.["Cosmos:Key"]
    
    // I don't know about this
    let dbConnectionService (svc: IServiceCollection) =
        svc.AddSingleton<ConnectionOperation>(fun _ -> Db.getRecipesConnection cosmosUri cosmosKey)
    
    if String.IsNullOrWhiteSpace(cosmosUri) || String.IsNullOrWhiteSpace(cosmosKey) then
        failwith "Invalid cosmos configuration"
    
    webHost args {
        add_service dbConnectionService
        endpoints [ all "/" [ GET, Response.ofHtml form; POST, handle ]
                    all "/recipes" [ GET, RecipesController.index; POST, RecipesController.save ]
                    get "/recipes/new" RecipesController.create ]
    }
    0
