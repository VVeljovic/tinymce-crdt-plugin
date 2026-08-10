using CrdtCore;

//var node1 = new CrdtDocument();
//var node2 = new CrdtDocument();


//node1.LocalInsert('H', 0);
//node1.LocalInsert('E', 1);
//node1.LocalInsert('L', 2);
//node1.LocalInsert('O', 3);

//foreach (var el in node1.Elements)
//{
//    node2.Elements.Add(el);
//}

//var el1 = node1.LocalInsert('L', 3);

//var el2 = node2.LocalInsert('!', 4);

//node1.RemoteInsert(el2);
//node2.RemoteInsert(el1);


//Console.WriteLine(node1.GetText());
//Console.WriteLine(node2.GetText());
//Console.ReadLine();

var node1 = new CrdtDocument();
var node2 = new CrdtDocument();

node1.LocalInsert('a', 0);
node1.LocalInsert('b', 1);
node1.LocalInsert('c', 2);
foreach (var el in node1.Elements)
{
    node2.Elements.Add(el);
}

var n11 = node1.LocalInsert('x', 1);
var n12 = node1.LocalInsert('y', 2);

var n21 = node2.LocalInsert('p', 1);
var n22 = node2.LocalInsert('q', 2);



node1.RemoteInsert(n21);
node1.RemoteInsert(n22);

node2.RemoteInsert(n11);
node2.RemoteInsert(n12);


Console.WriteLine(node1.GetText());
Console.WriteLine(node2.GetText());
Console.ReadLine();