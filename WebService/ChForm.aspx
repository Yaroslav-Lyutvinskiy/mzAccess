<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="ChForm.aspx.cs" Inherits="mzAccess.ChForm" %>

<%@ Register assembly="System.Web.DataVisualization, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35" namespace="System.Web.UI.DataVisualization.Charting" tagprefix="asp" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title></title>
    <style type="text/css">
        #Table1 {
            width: 1029px;
        }
        .auto-style1 {
            width: 80px;
        }
        .auto-style2 {
            width: 1045px;
        }
        .auto-style3 {
            width: 77px;
        }
    </style>
</head>
<body>
    <form id="form1" runat="server" style="font-family: Verdana, Geneva, Tahoma, sans-serif">
    <div style="width: 1029px">
        <table id="Table1" runat="server" height="460px" font-names="Verdana" class="auto-style2" >
            <tr runat="server">
                <td runat="server" height="460px" horizontalalign="Center" verticalalign="Top" width="600px">
                        <asp:Chart ID="Chart1" runat="server" BorderlineColor="#3366FF" BorderlineDashStyle="Solid" Width="600px" Height="460px">
                            <series>
                                <asp:Series Name="Series1" ChartType="FastLine" Color="Blue">
                                </asp:Series>
                            </series>
                            <chartareas>
                                <asp:ChartArea Name="ChartArea1">
                                    <AxisY Title="Intensity" TitleFont="Microsoft Sans Serif, 9pt">
                                    </AxisY>
                                    <AxisX IntervalOffsetType="Number" IsStartedFromZero="False" Title="Retention Time, (min)" TitleFont="Microsoft Sans Serif, 9pt">
                                    </AxisX>
                                </asp:ChartArea>
                            </chartareas>
                        </asp:Chart>
                </td>
                <td runat="server" valign="Top" align="Center">
                    <table class="controls">
                        <tr>
                            <td class="auto-style3">Min RT:</td>
                            <td><asp:TextBox ID="TextBox1" runat="server" AutoPostBack="True" Width="100px">5.0</asp:TextBox></td>
                            <td class="auto-style1">Max RT:</td>
                            <td><asp:TextBox ID="TextBox2" runat="server" AutoPostBack="True" Width="100px">10.0</asp:TextBox></td>
     					</tr>
                        <tr>
                            <td class="auto-style3">Min MZ:</td>
                            <td><asp:TextBox ID="TextBox3" runat="server" AutoPostBack="True" Width="100px">110.0</asp:TextBox></td>
                            <td class="auto-style1">Max MZ:</td>
                            <td><asp:TextBox ID="TextBox4" runat="server" AutoPostBack="True" Width="100px">111.0</asp:TextBox></td>
     					</tr>
        			</table>
                    <asp:CheckBoxList ID="CheckBoxList1" runat="server" AutoPostBack="True" ForeColor="Black" >
                        <asp:ListItem Selected="True">001_MOO_Labeling_HIL_24h_0_1</asp:ListItem>
                        <asp:ListItem Selected="True">002_MOO_Labeling_HIL_24h_0_2</asp:ListItem>
                    </asp:CheckBoxList>
                </td>
            </tr>
        </table>
    </div>
    </form>
</body>
</html>
