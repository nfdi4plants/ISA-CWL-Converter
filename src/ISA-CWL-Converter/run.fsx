#r @"C:\Users\Adrian\Documents\Code\DataPLANT\ISA-CWL-Converter\src\ISA-CWL-Converter\bin\Release\net6.0\ISA-CWL-Converter.dll"
#r @"nuget: ISADotNet.XLSX"
#r @"nuget: CWLDotnet"
open System
open System.IO
open System.Text.Json
open ISA_CWL_Converter
let assayFileUri = new Uri (Path.GetFullPath @"C:\Users\Adrian\Documents\Code\DataPLANT\ISA-CWL-Converter\src\ISA-CWL-Converter\data\isa.assay.neu.xlsx")

let assay = Converter.readAssayFromFile assayFileUri.AbsolutePath

let tools = Converter.generateTools assay
let fewf = tools.Head.Save()
let x = JsonSerializer.Serialize fewf
File.WriteAllText (@".\test.json", x) |> ignore