namespace ISA_CWL_Converter
#nowarn "3391"
module Converter =

    open System.IO
    open System.Collections.Generic
    open ISADotNet.XLSX
    open ISADotNet
    open ISADotNet.QueryModel
    open FSharpAux
    open Newtonsoft.Json


    type CWLDataType =
        | ArrayIn of CWLDotNet.CommandInputArraySchema
        | ArrayOut of CWLDotNet.CommandOutputArraySchema
        | Single of CWLDotNet.CWLType
    
    type CWLInput = {id: string; position: int; prefix: string; inputType: CWLDataType }
    type CWLOutput = {id: string; glob: string; outputType: CWLDataType }

    type cwlreq = 
        OneOf.OneOf<
            CWLDotNet.InlineJavascriptRequirement,
            CWLDotNet.SchemaDefRequirement,
            CWLDotNet.LoadListingRequirement,
            CWLDotNet.DockerRequirement,
            CWLDotNet.SoftwareRequirement,
            CWLDotNet.InitialWorkDirRequirement,
            CWLDotNet.EnvVarRequirement,
            CWLDotNet.ShellCommandRequirement,
            CWLDotNet.ResourceRequirement,
            CWLDotNet.WorkReuse,
            CWLDotNet.NetworkAccess,
            CWLDotNet.InplaceUpdateRequirement,
            CWLDotNet.ToolTimeLimit,
            CWLDotNet.SubworkflowFeatureRequirement,
            CWLDotNet.ScatterFeatureRequirement,
            CWLDotNet.MultipleInputFeatureRequirement,
            CWLDotNet.StepInputExpressionRequirement
        >

    let mapStringToCWLReq (req: string) (value: string option) : cwlreq =
        match req with 
        | "NetworkAccess" -> CWLDotNet.NetworkAccess(networkAccess= true)
        | "InitialWorkDirRequirement" -> 
            let dirent = JsonConvert.DeserializeObject<List<CWLDotNet.Dirent>> value.Value
            let listing = new List<OneOf.OneOf<OneOf.Types.None, CWLDotNet.Dirent, System.String, CWLDotNet.File, CWLDotNet.Directory, List<OneOf.OneOf<CWLDotNet.File, CWLDotNet.Directory>>>>()
            printfn "%A" (dirent.Item 0)
            listing.Add (dirent.Item 0)
            printfn "%A" (listing.Item 0)
            printfn "aaaa"
            CWLDotNet.InitialWorkDirRequirement(listing = listing)
        | "DockerRequirement" -> JsonConvert.DeserializeObject<CWLDotNet.DockerRequirement> value.Value
        | _ -> failwith "Error, wrong req"

    let readAssayFromFile (path: string) : ISADotNet.QueryModel.QProcessSequence =
        let _, assay = ISADotNet.XLSX.AssayFile.Assay.fromFile path
        ISADotNet.QueryModel.QAssay.fromAssay assay

    let createCommandInputParameter cwlInput =
        match cwlInput.inputType with
        | Single x ->
            CWLDotNet.CommandInputParameter(``type``= x, id=cwlInput.id, inputBinding=CWLDotNet.CommandLineBinding(position=cwlInput.position, prefix =cwlInput.prefix))
        | ArrayIn x -> 
            CWLDotNet.CommandInputParameter(``type``= x, id=cwlInput.id, inputBinding=CWLDotNet.CommandLineBinding(position=cwlInput.position, prefix =cwlInput.prefix))

    let createCommandOutputParameter cwlOutput =
        match cwlOutput.outputType with
        | Single x ->
            CWLDotNet.CommandOutputParameter(``type``= x, id=cwlOutput.id, outputBinding=CWLDotNet.CommandOutputBinding(glob=cwlOutput.glob))
        | ArrayOut x -> 
            CWLDotNet.CommandOutputParameter(``type``= x, id=cwlOutput.id, outputBinding=CWLDotNet.CommandOutputBinding(glob=cwlOutput.glob))
            
    let generateCWLCommandLineTool (id:string) (baseCommand:string[]) (inputs: seq<CWLInput>) (reqs : List<cwlreq>) (outputs: seq<CWLOutput>)=
        let cwlInputs =
            inputs 
            |> Seq.map(fun x ->
                createCommandInputParameter x
            )
            |> Seq.toList
        let cwlOutputs =
            outputs
            |> Seq.map(fun x ->
                createCommandOutputParameter x
            )
            |> Seq.toList
        let clt: CWLDotNet.CommandLineTool = CWLDotNet.CommandLineTool(id=id, baseCommand=new ResizeArray<string>(baseCommand), inputs=ResizeArray<CWLDotNet.CommandInputParameter>cwlInputs, outputs=ResizeArray<CWLDotNet.CommandOutputParameter>cwlOutputs, cwlVersion=CWLDotNet.CWLVersion.V1_2, requirements=reqs)
        clt

    let commonSubstring (names: string list) =
        let first' = names |> List.tryFind (fun _ -> true)
        let isWhiteSpace = System.String.IsNullOrWhiteSpace
    
        let mapper substringLength (first:string) currentStrings offset =
            if substringLength + offset <= first.Length then
                let currentSubstring = first.Substring(offset, substringLength)
                if not(isWhiteSpace(currentSubstring)) &&
                   not(currentStrings |> List.exists(fun f -> f = currentSubstring)) then
                    currentSubstring :: currentStrings
                else currentStrings
            else
                currentStrings

        match first' with
        | Some(first) ->
            [first.Length - 1 .. -1 .. 0]
            |> List.map(fun substringLength ->
                [0 .. first.Length]
                |> List.fold (mapper substringLength first) List.Empty)
            |> List.concat
            |> List.sortBy(fun s -> s)
            |> List.sortBy(fun s -> -s.Length)
            |> List.filter(fun s -> names |> Seq.forall(fun c -> c.Contains(s)))
        | None -> List.empty
        
    let getBaseCommandFromSheet (sheet: ISADotNet.QueryModel.QSheet) =
        let baseCommandList = 
            (sheet.Values.Components "BaseCommand").Values().Values
            |> List.distinct
        if baseCommandList.Length <> 1 then
            raise (System.Exception("Error, BaseCommand is not unique"))
        match baseCommandList with
        | baseCommandList when baseCommandList.Length > 0 ->
            match baseCommandList.Head with
            | ISADotNet.QueryModel.ISAValue.Component comp ->
                match comp.ValueText with
                | text when comp.ValueText.Length > 0 ->
                    Some(text.Split(' '))
                | _ -> None
            | _ -> failwith "Error, no component" // Should never happen?
        | _ -> failwith "Error, BaseCommand not Found"

    let getMainFileInputFromSheet (sheet: ISADotNet.QueryModel.QSheet) =
        let prefixCharacteristic = (sheet.Item 0).Characteristics().Characteristics("Prefix").Values.Head
        let inputTypeCharacteristic = (sheet.Item 0).Characteristics().Characteristics("InputType").Values.Head
        let prefix =
            match prefixCharacteristic with
            | ISADotNet.QueryModel.ISAValue.Characteristic char ->
                char.ValueText
            | _ -> failwith "Error, no characteristic" // Should never happen?
        let inputType =
            match inputTypeCharacteristic with
            | ISADotNet.QueryModel.ISAValue.Characteristic char ->
                char.ValueText
            | _ -> failwith "Error, no inputType" // Should never happen?
            |> fun input ->
                match input with
                | "File" -> CWLDotNet.CWLType.FILE
                | "Int" -> CWLDotNet.CWLType.INT
                | "Directory" -> CWLDotNet.CWLType.DIRECTORY
                | _ -> failwith "Error, wrong inputType"      
        { id = "input"; position = 0; prefix= prefix; inputType= Single inputType}

    let getInputParamtersFromSheet (sheet: ISADotNet.QueryModel.QSheet) =
        let isaParams = (sheet.Item 0).Parameters()
        let inputParams =
            isaParams |>
            Seq.mapi( fun index parameter ->
                match parameter with
                | ISADotNet.QueryModel.ISAValue.Parameter param ->
                    let id = param.NameText
                    let position = index + 1
                    let prefix = (param.ValueText.Split ' ').[0]
                    let inputType = 
                        printfn $"{param.ValueWithUnitText}"
                        (param.ValueWithUnitText.Split ' ').[2]
                        |> fun input ->
                            match input with
                            | "File" -> CWLDotNet.CWLType.FILE
                            | "Int" -> CWLDotNet.CWLType.INT
                            | "Directory" -> CWLDotNet.CWLType.DIRECTORY
                            | _ -> failwith "Error, wrong inputType"
                    {id=id; position = position; prefix=prefix; inputType= Single inputType}
                | _ -> failwith "Error, no param" // Should never happen?
            )
        inputParams

    let getRequirementsFromSheet  (sheet: ISADotNet.QueryModel.QSheet) =
        let isaParams = (sheet.Item 0).Values().Components()
        let reqs =
            isaParams
            |> Seq.fold(fun x y ->
                let comp =
                    match y with
                    | ISADotNet.QueryModel.ISAValue.Component comp ->
                        printfn "%s" comp.NameText
                        if comp.NameText.StartsWith "Requirement" then
                            Some(comp)
                        else
                            None
                    | _ -> failwith "Error, no component"
                match comp with
                | Some comp -> 
                    let split = comp.NameText.Split('[')[1]
                    let req = split.Substring(0, split.Length-1)
                    let newReq = mapStringToCWLReq req comp.ComponentName
                    newReq::x
                | None -> x
            )[]
        ResizeArray<cwlreq> reqs
        
    let getOutputFromSheet (sheet: ISADotNet.QueryModel.QSheet) = 
        let outputs = sheet.Outputs
        let output = outputs |> List.map fst
        let commonString = "*" + (commonSubstring output)[0] + "*"
        let output: CWLOutput = {id="out"; glob=commonString; outputType=ArrayOut (CWLDotNet.CommandOutputArraySchema(items = CWLDotNet.CWLType.FILE, ``type`` = CWLDotNet.enum_d062602be0b4b8fd33e69e29a841317b6ab665bc.ARRAY)) }
        output

    let generateTools (assay:ISADotNet.QueryModel.QProcessSequence) =
        assay.Sheets
        |> List.fold (fun x y ->
            let baseCommand = getBaseCommandFromSheet y
            let reqs = getRequirementsFromSheet y
            let mainFileInput = getMainFileInputFromSheet y
            let inputParams = getInputParamtersFromSheet y |> Seq.append [mainFileInput]
            let output = getOutputFromSheet y
            let cwlTool = generateCWLCommandLineTool y.SheetName baseCommand.Value inputParams reqs [output]
            cwlTool::x
        ) []