#r @".\bin\Release\net6.0\ISA-CWL-Converter.dll"
#r @"nuget: ISADotNet.XLSX"
#r @"nuget: ISADotNet"
#r @"nuget: CWLDotnet"
#r "nuget: ISADotNet.QueryModel, 0.6.0"
#r "nuget: Newtonsoft.Json, 13.0.2-beta2"

open System
open System.IO
open System.Text.Json
open ISA_CWL_Converter
open System.IO
open System.Collections.Generic
open ISADotNet.XLSX
open ISADotNet
open ISADotNet.QueryModel

let assayFileUri = new Uri (Path.GetFullPath @"C:\Users\jonat\OneDrive\Doktor\ISA-CWL-ConverterTest\test.xlsx")

let assay = Converter.readAssayFromFile assayFileUri.AbsolutePath

let tools = Converter.generateTools assay
let fewf = tools.Head.Save()
let x = JsonSerializer.Serialize fewf
File.WriteAllText (@".\test.cwl", x) |> ignore

let commonSubstring (names: string[]) =
    let first' = names |> Seq.tryFind (fun _ -> true)
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
        |> Seq.sortBy(fun s -> s)
        |> Seq.sortBy(fun s -> -s.Length)
        |> Seq.filter(fun s -> names |> Seq.forall(fun c -> c.Contains(s)))
    | None -> Seq.empty

    
let b = assay.Sheets |> List.head

(b.Item 0).Values().Components()
|> Seq.item 0
|> fun x -> 
    match x with
    | ISADotNet.QueryModel.ISAValue.Component comp ->
        comp.ComponentName
|> fun x -> Newtonsoft.Json.JsonConvert.DeserializeObject<CWLDotNet.DockerRequirement> x.Value
let a =
    commonSubstring [|
        "08_1_10_2_sol_2.mzlite"
        "08_2_10_2_sol_2.mzlite"
        "08_3_10_2_sol_2.mzlite"
        "08_1_11_4_sol_2.mzlite"
        "08_2_11_4_sol_2.mzlite"
        "08_3_11_4_sol_2.mzlite"
        "08_1_12_8_sol_2.mzlite"
        "08_2_12_8_sol_2.mzlite"
        "08_3_12_8_sol_2.mzlite"
        "08_2_13_-24_mem_1.mzlite"
        "08_3_13_-24_mem_1.mzlite"
        "08_1_14_0_mem_1.mzlite"
        "08_2_14_0_mem_1.mzlite"
        "08_3_14_0_mem_1.mzlite"
        "08_1_15_1_mem_1.mzlite"
        "08_2_15_1_mem_1.mzlite"
        "08_3_15_1_mem_1.mzlite"
        "08_1_16_2_mem_1.mzlite"
        "08_2_16_2_mem_1.mzlite"
        "08_3_16_2_mem_1.mzlite"
    |]
    |> Seq.toArray
    
let fewf = Converter.generateTools assay
fewf.Head.Save()
|> JsonSerializer.Serialize
|> fun x -> File.WriteAllText (@".\test.cwl",x)
