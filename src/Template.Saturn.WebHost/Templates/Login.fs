﻿module Login

open Giraffe.GiraffeViewEngine
open Microsoft.AspNetCore.Http
open System.Security.Claims


let login (ctx:HttpContext) =
    let name = 
        ctx.User.Claims
        |> Seq.filter (fun claim -> claim.Type = ClaimTypes.Name) 
        |> Seq.head
    [
        section [_class "hero is-light"] [
            div [_class "hero-body"] [
                div [_class "container"] [
                    p [_class "title"] [rawText "The sleeper has awakened."]
                    p [_class "subtitle"] [rawText name.Value]
                ]
            ]
        ]       
    ]

let layout ctx =
    App.layout (login ctx) ctx