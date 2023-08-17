module Recipes.UI

open Falco.Markup

module Layouts =
    let private head (htmlTitle : string) : XmlNode list =
        [
            Elem.meta [ Attr.charset "UTF-8" ]
            Elem.meta [ Attr.name "viewport"; Attr.content "width=device-width, initial-scale=1" ]
            Elem.title [] [Text.rawf $"%s{htmlTitle} | RecipeBox"]
            Elem.link [ Attr.rel "stylesheet"; Attr.href "/styles/tailwind.output.css" ]
        ]
        
    let master (htmlTitle : string) (content : XmlNode list) =
        Elem.html [ Attr.lang "en" ] [
            Elem.head [] (head htmlTitle)
            Elem.body [] [
                Elem.main [] content
            ]
        ]