﻿namespace CurricularAffairs

open Microsoft.AspNetCore.Http
open FSharp.Control.Tasks.ContextInsensitive
open Config
open Authorization
open Saturn
open Giraffe
open Microsoft.Extensions.Logging


module Controller =

    let indexAction (ctx : HttpContext) =
        let logger = ctx.GetLogger("CurricularAffairs.index")
        logger.LogInformation("EXAMPLE OF HOW TO USE LOGGING")
        task {
            //EXAMPLE OF HOW TO USE CONFIG
            let cnf = Controller.getConfig ctx
            let conStr = cnf.connectionString
            let configSettingExample = cnf.configSettingExample            
            return Views.index ctx { id = 10000L; gradYear = GradYear.Y2018; name = "Nervous System"; catalogNumber = 999}
        }

    let deleteAction (ctx : HttpContext) (id : string) = 
        let logger = ctx.GetLogger("CurricularAffairs.delete")
        logger.LogInformation("EXAMPLE OF HOW TO USE DELETE")
        logger.LogInformation(sprintf "DELETE THIS ID? %s" id)
        task {
            return Controller.text ctx (sprintf "NUMBER %s IS ALIVE!" id)
        }

    let resource = controller {
        //plug [Index] (pipeline { requires_role "admin" accessDenied })
        plug [Index; Show] (pipeline { requires_role_of (getAuthorizedRoles CurricularAffairs Access.View)  accessDenied })
        plug [Add; Create] (pipeline { requires_role_of (getAuthorizedRoles CurricularAffairs Access.Create)  accessDenied })
        plug [Edit; Update; Patch] (pipeline { requires_role_of (getAuthorizedRoles CurricularAffairs Access.Update)  accessDenied })
        //plug [Delete; DeleteAll] (pipeline { requires_role_of (getAuthorizedRoles CurricularAffairs Access.Delete)  accessDenied })
        index indexAction
        delete deleteAction
    }