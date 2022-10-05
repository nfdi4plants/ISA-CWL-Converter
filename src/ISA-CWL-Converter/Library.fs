namespace ISA_CWL_Converter

module Converter =

    open System.IO
    open System.Collections.Generic
    open ISADotNet.XLSX
    open ISADotNet
    open ISADotNet.QueryModel

    type CWLType =
    | File
    | Number

    type CWLInput = {id: string; position: int; prefix: string; inputType: CWLType }
    type CWLOutput = {id: string; glob: string; outputType: CWLType }
    type cwlreq = OneOf.OneOf<CWLDotNet.InlineJavascriptRequirement, CWLDotNet.SchemaDefRequirement, CWLDotNet.LoadListingRequirement, CWLDotNet.DockerRequirement, CWLDotNet.SoftwareRequirement, CWLDotNet.InitialWorkDirRequirement, CWLDotNet.EnvVarRequirement, CWLDotNet.ShellCommandRequirement, CWLDotNet.ResourceRequirement, CWLDotNet.WorkReuse, CWLDotNet.NetworkAccess, CWLDotNet.InplaceUpdateRequirement, CWLDotNet.ToolTimeLimit, CWLDotNet.SubworkflowFeatureRequirement, CWLDotNet.ScatterFeatureRequirement, CWLDotNet.MultipleInputFeatureRequirement, CWLDotNet.StepInputExpressionRequirement>

    let mapStringToCWLReq (req: string) (value: string) : cwlreq =
        match req with 
        | "NetworkAccess" ->CWLDotNet.NetworkAccess(networkAccess= true)
        | "InitialWorkDir" -> CWLDotNet.InitialWorkDirRequirement(listing="")
        | _ -> failwith "Error, wrong req"

    let readAssayFromFile (path: string) : ISADotNet.QueryModel.QProcessSequence =
        let _, assay = ISADotNet.XLSX.AssayFile.Assay.fromFile path
        ISADotNet.QueryModel.QAssay.fromAssay assay

    let generateCWLCommandLineTool (id:string) (baseCommand:string[]) (inputs: seq<CWLInput>) (reqs : List<cwlreq>) (outputs: seq<CWLOutput>)=
        let cwlInputs =
            inputs |>
            Seq.map(fun x ->
                CWLDotNet.CommandInputParameter(``type``=CWLDotNet.CWLType.FILE, id=x.id, inputBinding=CWLDotNet.CommandLineBinding(position=x.position, prefix =x.prefix))
            )
            |> Seq.toList
        let cwlOutputs =
            outputs |>
            Seq.map(fun x ->
                CWLDotNet.CommandOutputParameter(``type``= CWLDotNet.CWLType.FILE, id=x.id, outputBinding=CWLDotNet.CommandOutputBinding(glob=x.glob))
            )
            |> Seq.toList
        let clt: CWLDotNet.CommandLineTool = CWLDotNet.CommandLineTool(id=id, baseCommand=new ResizeArray<string>(baseCommand), inputs=ResizeArray<CWLDotNet.CommandInputParameter>cwlInputs, outputs=ResizeArray<CWLDotNet.CommandOutputParameter>cwlOutputs, cwlVersion=CWLDotNet.CWLVersion.V1_2, requirements=reqs)
        clt

    let getBaseCommandFromSheet (sheet: ISADotNet.QueryModel.QSheet) =
        let baseCommandList = (sheet.Values.Components "BaseCommand").Values().Values
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
        let prefix =
            match prefixCharacteristic with
            | ISADotNet.QueryModel.ISAValue.Characteristic char ->
                char.ValueText
            | _ -> failwith "Error, no characteristic" // Should never happen?
        { id = "input"; position = 0; prefix= prefix; inputType=CWLType.File}

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
                    let inputType = CWLType.File // TODO: Type via Unit
                    {id=id; position = position; prefix=prefix; inputType=inputType}
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
                        if comp.NameText.StartsWith "SoftwareRequirement" then
                            Some(comp)
                        else
                            None
                    | _ -> failwith "Error, no component"
                match comp with
                | Some comp -> 
                    let split = comp.NameText.Split('[')[1]
                    let req = split.Substring(0, split.Length-1)
                    let newReq = mapStringToCWLReq req comp.ValueText
                    let newX = x |> List.append([newReq])
                    newX
                | None -> x
            )[]
        ResizeArray<cwlreq> reqs

    let getOutputFromSheet (sheet: ISADotNet.QueryModel.QSheet) = 
        let outputs = sheet.Outputs
        let output, _ = outputs.Head
        let extension = "*" + Path.GetExtension output;
        let output: CWLOutput = {id="out"; glob=extension; outputType=CWLType.File }
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
            List.append x [cwlTool]
        ) []