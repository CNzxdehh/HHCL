import io

p = 'HHCL/Views/LanView.xaml'
s = io.open(p, encoding='utf-8').read()

# ============ 1) 客机面板：加"搜索附近房间" + 房间列表 ============
old = '''                <Border Style="{StaticResource Divider}" Margin="0,16,0,0" />
                <TextBlock x:Name="JoinStateText" Text="填好地址点「直连进入」即可。如果对方不在同一个局域网，需要他自己做端口转发。"
                           Style="{StaticResource Label}" Margin="0,12,0,0" TextWrapping="Wrap" />'''
new = '''                <!-- 自动发现附近房间：不用手抄 IP -->
                <Border Style="{StaticResource Divider}" Margin="0,16,0,0" />
                <Grid Margin="0,12,0,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <TextBlock Text="附近的房间" Style="{StaticResource H2}" FontSize="13.5" VerticalAlignment="Center" />
                        <TextBlock x:Name="ScanStateText" Text="" Style="{StaticResource Label}" Margin="8,0,0,0"
                                   VerticalAlignment="Center" />
                    </StackPanel>
                    <Button Grid.Column="1" x:Name="ScanBtn" Content="搜索房间" Style="{StaticResource GhostButton}"
                            Height="30" Width="104" Click="Scan_Click" />
                </Grid>

                <ListBox x:Name="RoomList" Style="{StaticResource FluentListBox}" Margin="0,8,0,0" MaxHeight="150"
                         HorizontalContentAlignment="Stretch" Visibility="Collapsed"
                         SelectionChanged="RoomList_SelectionChanged">
                    <ListBox.ItemTemplate>
                        <DataTemplate>
                            <Grid Margin="0,4">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <StackPanel>
                                    <TextBlock Text="{Binding Address}" FontSize="13" FontWeight="SemiBold" />
                                    <TextBlock Style="{StaticResource Label}" FontSize="11" Margin="0,2,0,0">
                                        <Run Text="房间码" /><Run Text="{Binding Code, Mode=OneWay}" />
                                    </TextBlock>
                                </StackPanel>
                                <Button Grid.Column="1" Content="加入" Style="{StaticResource GhostButton}"
                                        Height="28" Width="72" VerticalAlignment="Center"
                                        Tag="{Binding Address}" Click="JoinRoom_Click" />
                            </Grid>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                </ListBox>

                <TextBlock x:Name="JoinStateText" Text="填好地址点「直连进入」即可；或者点「搜索房间」自动找同一局域网里的房间。"
                           Style="{StaticResource Label}" Margin="0,12,0,0" TextWrapping="Wrap" />'''
assert old in s, 'join panel anchor'
s = s.replace(old, new, 1)

# ============ 2) 主机面板："给朋友的话"加复制按钮 ============
old2 = '''                        <TextBlock Text="给朋友的话" Style="{StaticResource Label}" />
                        <TextBlock x:Name="ShareText" Text="" Style="{StaticResource Body}" Margin="0,6,0,0" TextWrapping="Wrap" />'''
new2 = '''                        <Grid Margin="0,12,0,0">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition Width="Auto" />
                            </Grid.ColumnDefinitions>
                            <TextBlock Text="给朋友的话" Style="{StaticResource Label}" VerticalAlignment="Center" />
                            <Button Grid.Column="1" x:Name="CopyShareBtn" Content="复制这段话" Style="{StaticResource GhostButton}"
                                    Height="28" Width="104" Click="CopyShare_Click" />
                        </Grid>
                        <TextBlock x:Name="ShareText" Text="" Style="{StaticResource Body}" Margin="0,6,0,0" TextWrapping="Wrap" />'''
assert old2 in s, 'share text anchor'
s = s.replace(old2, new2, 1)

io.open(p, 'w', encoding='utf-8').write(s)
print('LanView XAML: 搜索房间 + 复制话术 已加')
